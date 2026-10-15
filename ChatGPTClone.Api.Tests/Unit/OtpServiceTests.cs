using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ChatGPTClone.Api.Tests.Unit;

public partial class OtpServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryOtpRepository _repository = new();
    private readonly CapturingEmailSender _email = new();
    private const string Email = "user@example.test";
    private readonly OtpService _service;

    public OtpServiceTests()
    {
        var options = Options.Create(new OtpOptions
        {
            ExpiryMinutes = 5,
            ResendCooldownSeconds = 60,
            MaxAttempts = 5,
            MaxSendsPerHour = 5,
            HashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });
        _service = new OtpService(_repository, new OtpHasher(options), _email, options, _time, NullLogger<OtpService>.Instance);
    }

    [Fact]
    public async Task Issue_emails_a_six_digit_code_and_stores_only_its_hash()
    {
        var result = await _service.IssueAsync(Email, "Test User", explicitResend: false, default);

        Assert.True(result.CodeSent);
        Assert.Equal(300, result.ExpiresInSeconds);
        Assert.Matches(@"^\d{6}$", _email.LastCode);
        var stored = Assert.Single(_repository.Items);
        Assert.DoesNotContain(_email.LastCode!, stored.OtpHash);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddMinutes(5), stored.ExpiresAt);
    }

    [Fact]
    public async Task Correct_code_verifies_once_and_is_then_single_use()
    {
        await _service.IssueAsync(Email, null, false, default);
        var code = _email.LastCode!;

        await _service.VerifyAsync(Email, code, default);

        var reuse = await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, code, default));
        Assert.Equal(400, reuse.StatusCode);
        Assert.Equal("expired", reuse.Extensions["reason"]);
    }

    [Fact]
    public async Task Wrong_code_reports_remaining_attempts_and_locks_after_the_limit()
    {
        await _service.IssueAsync(Email, null, false, default);
        var code = _email.LastCode!;
        var wrong = code == "000000" ? "111111" : "000000";

        for (var expectedRemaining = 4; expectedRemaining >= 1; expectedRemaining--)
        {
            var ex = await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, wrong, default));
            Assert.Equal(expectedRemaining, ex.Extensions["attemptsRemaining"]);
        }

        var last = await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, wrong, default));
        Assert.Equal("too_many_attempts", last.Extensions["reason"]);

        // Even the right code no longer works once the code is burned.
        await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, code, default));
    }

    [Fact]
    public async Task Code_expires_after_five_minutes()
    {
        await _service.IssueAsync(Email, null, false, default);
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, _email.LastCode!, default));
        Assert.Equal("expired", ex.Extensions["reason"]);
    }

    [Fact]
    public async Task Resend_within_cooldown_is_rejected_with_retry_after()
    {
        await _service.IssueAsync(Email, null, false, default);
        _time.Advance(TimeSpan.FromSeconds(20));

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.IssueAsync(Email, "Test User", explicitResend: true, default));
        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(40, ex.Extensions["retryAfterSeconds"]);
    }

    [Fact]
    public async Task Signing_in_again_during_cooldown_reuses_the_code_already_sent()
    {
        await _service.IssueAsync(Email, null, false, default);
        _time.Advance(TimeSpan.FromSeconds(10));

        var again = await _service.IssueAsync(Email, "Test User", explicitResend: false, default);

        Assert.False(again.CodeSent);
        Assert.Equal(1, _email.SentCount);
    }

    [Fact]
    public async Task A_new_code_invalidates_the_previous_one()
    {
        await _service.IssueAsync(Email, null, false, default);
        var oldCode = _email.LastCode!;
        _time.Advance(TimeSpan.FromSeconds(61));
        await _service.IssueAsync(Email, "Test User", explicitResend: true, default);
        var newCode = _email.LastCode!;

        if (oldCode != newCode)
        {
            await Assert.ThrowsAsync<ApiException>(() => _service.VerifyAsync(Email, oldCode, default));
        }
        await _service.VerifyAsync(Email, newCode, default);
    }

    [Fact]
    public async Task Hourly_cap_blocks_further_codes()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.IssueAsync(Email, "Test User", explicitResend: true, default);
            _time.Advance(TimeSpan.FromSeconds(61));
        }

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.IssueAsync(Email, "Test User", explicitResend: true, default));
        Assert.Equal(429, ex.StatusCode);
    }

    [Fact]
    public async Task Failed_email_returns_503_and_the_unsent_code_is_not_usable()
    {
        _email.Fail = true;

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.IssueAsync(Email, null, false, default));

        Assert.Equal(503, ex.StatusCode);
        Assert.All(_repository.Items, o => Assert.True(o.IsUsed));
    }

    // ----- Test doubles -----

    private sealed partial class CapturingEmailSender : IEmailSender
    {
        public bool Fail { get; set; }
        public string? LastCode { get; private set; }
        public int SentCount { get; private set; }

        public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string textBody,
            CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new EmailDeliveryException("simulated", new InvalidOperationException());
            }
            LastCode = CodePattern().Match(textBody).Groups[1].Value;
            SentCount++;
            return Task.CompletedTask;
        }

        [GeneratedRegex(@"code is: (\d{6})")]
        private static partial Regex CodePattern();
    }

    /// <summary>Same semantics as OtpRepository (including the atomic conditional updates), in memory.</summary>
    private sealed class InMemoryOtpRepository : IOtpRepository
    {
        private int _nextId = 1;
        public List<OtpVerification> Items { get; } = [];

        /// <summary>Returns a snapshot, like EF: later ExecuteUpdate-style changes don't alter what the caller read.</summary>
        public Task<OtpVerification?> GetLatestActiveAsync(string email, DateTime utcNow, CancellationToken ct)
        {
            var found = Items.Where(o => o.Email == email && !o.IsUsed && o.ExpiresAt > utcNow)
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.OtpVerificationId).FirstOrDefault();
            return Task.FromResult(found is null ? null : new OtpVerification
            {
                OtpVerificationId = found.OtpVerificationId,
                Email = found.Email,
                OtpHash = found.OtpHash,
                ExpiresAt = found.ExpiresAt,
                IsUsed = found.IsUsed,
                AttemptCount = found.AttemptCount,
                CreatedAt = found.CreatedAt,
            });
        }

        public Task<DateTime?> GetLatestCreatedAtAsync(string email, CancellationToken ct) =>
            Task.FromResult(Items.Where(o => o.Email == email).Select(o => (DateTime?)o.CreatedAt).Max());

        public Task<List<DateTime>> GetCreatedTimesSinceAsync(string email, DateTime sinceUtc, CancellationToken ct) =>
            Task.FromResult(Items.Where(o => o.Email == email && o.CreatedAt >= sinceUtc).Select(o => o.CreatedAt).Order().ToList());

        public Task<int> InvalidateAllActiveAsync(string email, CancellationToken ct)
        {
            var active = Items.Where(o => o.Email == email && !o.IsUsed).ToList();
            active.ForEach(o => o.IsUsed = true);
            return Task.FromResult(active.Count);
        }

        public Task<bool> TryMarkUsedAsync(int id, CancellationToken ct)
        {
            var otp = Items.Single(o => o.OtpVerificationId == id);
            if (otp.IsUsed)
            {
                return Task.FromResult(false);
            }
            otp.IsUsed = true;
            return Task.FromResult(true);
        }

        public Task<bool> TryIncrementAttemptAsync(int id, int maxAttempts, CancellationToken ct)
        {
            var otp = Items.Single(o => o.OtpVerificationId == id);
            if (otp.IsUsed || otp.AttemptCount >= maxAttempts)
            {
                return Task.FromResult(false);
            }
            otp.AttemptCount++;
            return Task.FromResult(true);
        }

        private OtpVerification? _pending;

        public void Add(OtpVerification otp) => _pending = otp;

        public Task SaveChangesAsync(CancellationToken ct)
        {
            if (_pending is not null)
            {
                // The real DbContext stamps CreatedAt; the service set ExpiresAt from the same clock.
                _pending.OtpVerificationId = _nextId++;
                _pending.CreatedAt = _pending.ExpiresAt.AddMinutes(-5);
                Items.Add(_pending);
                _pending = null;
            }
            return Task.CompletedTask;
        }
    }
}

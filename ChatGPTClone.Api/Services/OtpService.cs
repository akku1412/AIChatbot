using System.Security.Cryptography;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services;

public sealed class OtpService(
    IOtpRepository otpRepository,
    IOtpHasher hasher,
    IEmailSender emailSender,
    IOptions<OtpOptions> options,
    TimeProvider timeProvider,
    ILogger<OtpService> logger) : IOtpService
{
    private readonly OtpOptions _options = options.Value;

    public async Task<OtpIssueResult> IssueAsync(User user, bool explicitResend, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Resend cooldown.
        var lastCreatedAt = await otpRepository.GetLatestCreatedAtAsync(user.UserId, cancellationToken);
        var cooldownRemaining = lastCreatedAt is null
            ? 0
            : SecondsUntil(lastCreatedAt.Value.AddSeconds(_options.ResendCooldownSeconds), now);

        if (cooldownRemaining > 0)
        {
            if (explicitResend)
            {
                throw new ApiException(StatusCodes.Status429TooManyRequests, "Please wait before requesting a new code",
                    $"You can request a new code in {FormatWait(cooldownRemaining)}.",
                    new Dictionary<string, object?> { ["retryAfterSeconds"] = cooldownRemaining });
            }

            // Repeated sign-in moments after a code was sent: keep using the code already in the inbox.
            var active = await otpRepository.GetLatestActiveAsync(user.UserId, now, cancellationToken);
            if (active is not null)
            {
                return new OtpIssueResult(false, SecondsUntil(active.ExpiresAt, now), cooldownRemaining);
            }
        }

        // Hourly cap per user (on top of the per-IP rate limiter).
        var recent = await otpRepository.GetCreatedTimesSinceAsync(user.UserId, now.AddHours(-1), cancellationToken);
        if (recent.Count >= _options.MaxSendsPerHour)
        {
            var retryAfter = Math.Max(1, SecondsUntil(recent[0].AddHours(1), now));

            // On sign-in, a still-valid code already in the inbox is better than a hard stop.
            if (!explicitResend)
            {
                var active = await otpRepository.GetLatestActiveAsync(user.UserId, now, cancellationToken);
                if (active is not null)
                {
                    return new OtpIssueResult(false, SecondsUntil(active.ExpiresAt, now), retryAfter);
                }
            }

            throw new ApiException(StatusCodes.Status429TooManyRequests, "Too many codes requested",
                $"You have requested too many codes. Please try again in {FormatWait(retryAfter)}.",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfter });
        }

        // A new code always invalidates every older one.
        await otpRepository.InvalidateAllActiveAsync(user.UserId, cancellationToken);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = new OtpVerification
        {
            UserId = user.UserId,
            OtpHash = hasher.Hash(user.UserId, code),
            ExpiresAt = now.AddMinutes(_options.ExpiryMinutes),
        };
        otpRepository.Add(otp);
        await otpRepository.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(
                user.Email, user.FullName, OtpEmailTemplate.Subject,
                OtpEmailTemplate.Html(user.FullName, code, _options.ExpiryMinutes),
                OtpEmailTemplate.Text(user.FullName, code, _options.ExpiryMinutes),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            // The user never received this code, so it must not stay valid.
            await otpRepository.TryMarkUsedAsync(otp.OtpVerificationId, CancellationToken.None);
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Could not send the verification email",
                "We could not send the code to your email right now. Please try again in a few minutes.");
        }

        logger.LogInformation("Sent OTP {OtpVerificationId} to user {UserId}", otp.OtpVerificationId, user.UserId);

        return new OtpIssueResult(true, _options.ExpiryMinutes * 60, _options.ResendCooldownSeconds);
    }

    public async Task VerifyAsync(User user, string code, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var otp = await otpRepository.GetLatestActiveAsync(user.UserId, now, cancellationToken);

        if (otp is null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Code expired",
                "This code has expired or was already used. Please request a new code.",
                new Dictionary<string, object?> { ["reason"] = "expired" });
        }

        if (otp.AttemptCount >= _options.MaxAttempts)
        {
            await otpRepository.TryMarkUsedAsync(otp.OtpVerificationId, cancellationToken);
            throw TooManyAttempts();
        }

        if (hasher.Verify(user.UserId, code, otp.OtpHash))
        {
            // Atomic single-use: if two requests race with the right code, only one wins.
            if (!await otpRepository.TryMarkUsedAsync(otp.OtpVerificationId, cancellationToken))
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Code expired",
                    "This code was already used. Please request a new code.",
                    new Dictionary<string, object?> { ["reason"] = "expired" });
            }

            logger.LogInformation("OTP {OtpVerificationId} verified for user {UserId}", otp.OtpVerificationId, user.UserId);
            return;
        }

        if (!await otpRepository.TryIncrementAttemptAsync(otp.OtpVerificationId, _options.MaxAttempts, cancellationToken))
        {
            throw TooManyAttempts();
        }

        var attemptsRemaining = _options.MaxAttempts - (otp.AttemptCount + 1);
        logger.LogInformation("Wrong OTP for user {UserId}; {AttemptsRemaining} attempts remaining",
            user.UserId, attemptsRemaining);

        if (attemptsRemaining <= 0)
        {
            await otpRepository.TryMarkUsedAsync(otp.OtpVerificationId, cancellationToken);
            throw TooManyAttempts();
        }

        throw new ApiException(StatusCodes.Status400BadRequest, "Incorrect code",
            $"That code is not correct. You have {attemptsRemaining} attempt{(attemptsRemaining == 1 ? "" : "s")} left.",
            new Dictionary<string, object?> { ["reason"] = "incorrect", ["attemptsRemaining"] = attemptsRemaining });
    }

    private static ApiException TooManyAttempts() =>
        new(StatusCodes.Status400BadRequest, "Too many incorrect attempts",
            "This code is no longer valid because of too many incorrect attempts. Please request a new code.",
            new Dictionary<string, object?> { ["reason"] = "too_many_attempts", ["attemptsRemaining"] = 0 });

    /// <summary>"45 seconds", "1 minute", "12 minutes".</summary>
    internal static string FormatWait(int seconds)
    {
        if (seconds < 60)
        {
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    private static int SecondsUntil(DateTime targetUtc, DateTime nowUtc) =>
        Math.Max(0, (int)Math.Ceiling((targetUtc - nowUtc).TotalSeconds));
}

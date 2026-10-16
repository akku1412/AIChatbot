using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>
/// Runs the real API in memory against a throwaway SQL Server database (created from the EF migrations,
/// dropped afterwards). Only the external services are replaced: Google token validation, email, and AI.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestDatabase = "ChatGPTCloneDB_Tests";

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CHATGPTCLONE_TEST_DB")
        ?? $@"Server=AKANSHA\SQL2016;Database={TestDatabase};Trusted_Connection=True;TrustServerCertificate=True";

    public FakeEmailSender Email { get; } = new();
    public FakeAiProvider Ai { get; } = new();

    /// <summary>Private upload folder for this test run (deleted afterwards).</summary>
    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "chatgptclone-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Small limit so the "too large" path is cheap to test.</summary>
    public const long MaxFileSize = 1024 * 1024;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        builder.UseSetting("Otp:HashKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Otp:MaxSendsPerHour", "100");
        builder.UseSetting("Smtp:Username", "unused@example.test");
        builder.UseSetting("Smtp:Password", "unused");
        builder.UseSetting("Smtp:FromEmail", "unused@example.test");
        builder.UseSetting("Ai:Provider", "OpenAI");
        builder.UseSetting("RateLimits:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:AiChatPerMinute", "100000");
        builder.UseSetting("RateLimits:UploadsPerMinute", "100000");
        builder.UseSetting("FileUploads:StoragePath", StoragePath);
        builder.UseSetting("FileUploads:MaxFileSizeBytes", MaxFileSize.ToString());

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IGoogleTokenValidator, FakeGoogleTokenValidator>();
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<IAiChatProvider>(Ai);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync(); // also proves the migrations apply cleanly to an empty database
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }

    /// <summary>A browser-like client: HTTPS (so Secure cookies work) with its own cookie jar.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>Accepts "google|{sub}|{email}|{name}"; anything else is an invalid token.</summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        var parts = idToken.Split('|');
        return Task.FromResult(parts is ["google", var sub, var email, var name]
            ? new GoogleIdentity(sub, email.ToLowerInvariant(), name, null)
            : null);
    }
}

/// <summary>Captures the OTP from each email instead of sending it.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _lastCodeByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken)
    {
        _lastCodeByEmail[toEmail] = CodePattern().Match(textBody).Groups[1].Value;
        return Task.CompletedTask;
    }

    public string LastCodeFor(string email) =>
        _lastCodeByEmail.TryGetValue(email, out var code) ? code : throw new InvalidOperationException($"No email sent to {email}");

    [GeneratedRegex(@"code is: (\d{6})")]
    private static partial Regex CodePattern();
}

/// <summary>Deterministic AI: echoes the last user message; can be switched to fail.</summary>
public sealed class FakeAiProvider : IAiChatProvider
{
    public string Name => "Fake";
    public AiFailureKind? FailWith { get; set; }
    public ConcurrentBag<IReadOnlyList<AiChatMessage>> Prompts { get; } = [];

    /// <summary>The most recent prompt (tests in a collection run one at a time).</summary>
    public IReadOnlyList<AiChatMessage>? LastPrompt { get; private set; }

    public Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (FailWith is { } kind)
        {
            throw new AiProviderException(kind, "simulated failure");
        }

        if (messages[0].Content.StartsWith("Write a short title", StringComparison.Ordinal))
        {
            return Task.FromResult(new AiCompletion("\"Fake Generated Title\"", 5, false));
        }

        Prompts.Add(messages);
        LastPrompt = messages; // the answer prompt, not the title prompt
        var lastUser = messages.Last(m => m.Role == MessageRole.User).Content;
        return Task.FromResult(new AiCompletion($"**Echo:** {lastUser}", 42, false));
    }
}

/// <summary>Signs users in through the real endpoints.</summary>
public static class TestAuth
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Email OTP: send-otp -> read the code from the captured email -> verify-otp. Returns the access token.</summary>
    public static async Task<string> SignInAsync(ApiFactory factory, HttpClient client, string? email = null)
    {
        var session = await SignInWithEmailAsync(factory, client, email ?? NewEmail());
        return session.GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> SignInWithEmailAsync(ApiFactory factory, HttpClient client, string email)
    {
        var sent = await client.PostAsJsonAsync("/api/auth/send-otp", new { email });
        sent.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email, code = factory.Email.LastCodeFor(email) });
        verify.EnsureSuccessStatusCode();
        return await verify.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Google: the fake validator accepts "google|sub|email|name". Returns the HTTP response.</summary>
    public static Task<HttpResponseMessage> GoogleAsync(HttpClient client, string sub, string email, string name = "Google User") =>
        client.PostAsJsonAsync("/api/auth/google", new { idToken = $"google|{sub}|{email}|{name}" });

    /// <summary>POST /api/files/upload with one file in the "file" form field.</summary>
    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = form };
        request.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(request);
    }

    public static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

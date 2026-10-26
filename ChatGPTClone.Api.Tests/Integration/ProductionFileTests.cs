using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Services.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 21: abandoned-upload cleanup and the virus scan before storing.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductionFileTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Scanner.Reset();

    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static async Task<Guid> UploadOkAsync(User user, string name, string text)
    {
        var response = await UploadAsync(user.Client, user.Token, name, Encoding.UTF8.GetBytes(text));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("fileId").GetGuid();
    }

    private int StoredFileCount() => Directory.Exists(factory.StoragePath) ? Directory.GetFiles(factory.StoragePath).Length : 0;

    private async Task BackdateAsync(Guid fileId, TimeSpan age)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var when = DateTime.UtcNow - age;
        await db.UploadedFiles.Where(f => f.FileId == fileId).ExecuteUpdateAsync(s => s.SetProperty(f => f.CreatedAt, when));
    }

    private async Task<bool> ExistsAsync(Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UploadedFiles.AnyAsync(f => f.FileId == fileId);
    }

    // ---------- Cleanup ----------

    [Fact]
    public async Task Cleanup_deletes_only_old_uploads_that_were_never_used()
    {
        var user = await SignedInAsync();
        var abandoned = await UploadOkAsync(user, "forgotten.txt", "never sent");
        var recent = await UploadOkAsync(user, "just-now.txt", "still in the composer");
        var sent = await UploadOkAsync(user, "sent.txt", "attached to a message");
        var shared = await UploadOkAsync(user, "shared.txt", "in a project");

        var chat = (await (await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", user.Token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{chat}/messages", user.Token,
            new { content = "read this", attachmentIds = new[] { sent } }))).StatusCode);
        var project = (await (await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/projects", user.Token, new { name = "Keep" })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("projectId").GetInt32();
        await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/projects/{project}/files", user.Token, new { fileId = shared }));

        foreach (var old in new[] { abandoned, sent, shared })
        {
            await BackdateAsync(old, TimeSpan.FromHours(25));
        }
        var storedBefore = StoredFileCount();

        var deleted = await factory.Services.GetRequiredService<UploadCleanupWorker>().RunOnceAsync(CancellationToken.None);

        Assert.True(deleted >= 1);
        Assert.False(await ExistsAsync(abandoned));
        Assert.True(await ExistsAsync(recent));  // younger than 24 hours
        Assert.True(await ExistsAsync(sent));    // attached to a message
        Assert.True(await ExistsAsync(shared));  // shared with a project
        Assert.True(StoredFileCount() < storedBefore); // its bytes are gone too
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{abandoned}", user.Token))).StatusCode);
    }

    // ---------- Virus scan ----------

    [Fact]
    public async Task Infected_uploads_are_refused_with_422_and_never_stored()
    {
        var user = await SignedInAsync();
        var storedBefore = StoredFileCount();

        var response = await UploadAsync(user.Client, user.Token, "invoice.txt",
            Encoding.UTF8.GetBytes($"X5O!P%@AP[4\\PZX54(P^)7CC)7}}${FakeMalwareScanner.Marker}!$H+H*"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("malware_detected", problem.GetProperty("reason").GetString());
        Assert.Contains("Eicar-Test-Signature", problem.GetProperty("detail").GetString());
        Assert.Equal(storedBefore, StoredFileCount());
    }

    [Fact]
    public async Task Clean_uploads_are_scanned_then_stored()
    {
        var user = await SignedInAsync();
        var scansBefore = factory.Scanner.Scans;

        await UploadOkAsync(user, "notes.txt", "perfectly ordinary notes");

        Assert.Equal(scansBefore + 1, factory.Scanner.Scans);
    }

    [Fact]
    public async Task When_the_scanner_is_down_uploads_are_refused_by_default()
    {
        var user = await SignedInAsync();
        factory.Scanner.Unavailable = true;
        var storedBefore = StoredFileCount();

        var response = await UploadAsync(user.Client, user.Token, "notes.txt", "hello"u8.ToArray());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("scan_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());
        Assert.Equal(storedBefore, StoredFileCount());
    }

    [Fact]
    public async Task With_FailClosed_off_an_unreachable_scanner_lets_uploads_through()
    {
        factory.Scanner.Unavailable = true;
        using var failOpen = factory.WithWebHostBuilder(b => b.UseSetting("MalwareScan:FailClosed", "false"));
        var client = failOpen.CreateClient();
        var token = await SignInAsync(factory, client);

        var response = await UploadAsync(client, token, "notes.txt", "hello"u8.ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

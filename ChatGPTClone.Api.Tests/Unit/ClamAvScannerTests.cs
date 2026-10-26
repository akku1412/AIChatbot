using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Services.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>The clamd INSTREAM protocol, against a fake clamd on a local port.</summary>
public class ClamAvScannerTests
{
    /// <summary>Accepts one connection, reads the INSTREAM framing, answers with <paramref name="reply"/>.</summary>
    private static async Task<(int Port, Task<(string Command, byte[] Data)> Received)> FakeClamdAsync(Func<byte[], string> reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            listener.Stop();
            await using var net = client.GetStream();
            var command = new byte[10];
            await net.ReadExactlyAsync(command);
            using var data = new MemoryStream();
            var length = new byte[4];
            while (true)
            {
                await net.ReadExactlyAsync(length);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(length);
                if (size == 0)
                {
                    break;
                }
                var chunk = new byte[size];
                await net.ReadExactlyAsync(chunk);
                data.Write(chunk);
            }
            await net.WriteAsync(Encoding.ASCII.GetBytes(reply(data.ToArray()) + "\0"));
            return (Encoding.ASCII.GetString(command), data.ToArray());
        });
        await Task.Yield();
        return (port, received);
    }

    private static ClamAvScanner Scanner(int port) =>
        new(Options.Create(new MalwareScanOptions { Enabled = true, Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }),
            NullLogger<ClamAvScanner>.Instance);

    [Fact]
    public async Task Clean_files_are_streamed_in_chunks_and_pass()
    {
        var content = new byte[200_000]; // more than one 64 KB chunk
        new Random(1).NextBytes(content);
        var (port, received) = await FakeClamdAsync(_ => "stream: OK");

        var result = await Scanner(port).ScanAsync(new MemoryStream(content), CancellationToken.None);

        Assert.False(result.Infected);
        var (command, data) = await received;
        Assert.Equal("zINSTREAM\0", command);
        Assert.Equal(content, data); // every byte arrived, in order
    }

    [Fact]
    public async Task Infected_files_report_the_signature()
    {
        var (port, _) = await FakeClamdAsync(_ => "stream: Eicar-Test-Signature FOUND");

        var result = await Scanner(port).ScanAsync(new MemoryStream("X5O!P%@AP"u8.ToArray()), CancellationToken.None);

        Assert.True(result.Infected);
        Assert.Equal("Eicar-Test-Signature", result.Threat);
    }

    [Fact]
    public async Task Error_replies_and_unreachable_scanners_give_no_verdict()
    {
        var (port, _) = await FakeClamdAsync(_ => "INSTREAM size limit exceeded. ERROR");
        await Assert.ThrowsAsync<MalwareScanUnavailableException>(() => Scanner(port).ScanAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));

        // Nothing listens on this port any more.
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var unusedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await Assert.ThrowsAsync<MalwareScanUnavailableException>(() => Scanner(unusedPort).ScanAsync(new MemoryStream([1]), CancellationToken.None));
    }
}

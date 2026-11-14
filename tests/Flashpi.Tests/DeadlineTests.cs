using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Flashpi.Tests;

/// <summary>The deadline of an attempt covers reading the whole answer, on a real socket.</summary>
public sealed class DeadlineTests
{
    private const string Status = "{\"status\":\"operational\"}";

    [Fact]
    public async Task Times_out_an_answer_that_trickles_in_past_the_deadline()
    {
        using var server = new SlowServer(async stream =>
        {
            await SlowServer.WriteAsync(stream, $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Status.Length}\r\n\r\n");
            foreach (var character in Status)
            {
                await SlowServer.WriteAsync(stream, character.ToString());
                await Task.Delay(100);
            }
        });
        using var client = server.Client(TimeSpan.FromMilliseconds(400));
        await WarmUpAsync(() => client.Status.GetAsync());
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<ApiTimeoutException>(() => client.Status.GetAsync());
        Assert.Contains("timed out after 0.4 seconds", error.Message);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Times_out_headers_that_never_come()
    {
        using var server = new SlowServer(_ => Task.Delay(5000));
        using var client = server.Client(TimeSpan.FromMilliseconds(300));
        await WarmUpAsync(() => client.Status.GetAsync());
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<ApiTimeoutException>(() => client.Status.GetAsync());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Reads_an_answer_that_arrives_in_time()
    {
        using var server = new SlowServer(async stream =>
        {
            await SlowServer.WriteAsync(stream, $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Status.Length}\r\n\r\n");
            await Task.Delay(50);
            await SlowServer.WriteAsync(stream, Status);
        });
        using var client = server.Client(TimeSpan.FromSeconds(5));
        var status = await client.Status.GetAsync();
        Assert.Equal(Models.ApiStatusKind.Operational, status.Status);
    }

    [Fact]
    public async Task Cancelling_while_the_answer_trickles_in_cancels_the_call()
    {
        using var server = new SlowServer(async stream =>
        {
            await SlowServer.WriteAsync(stream, $"HTTP/1.1 200 OK\r\nContent-Length: {Status.Length}\r\n\r\n{{");
            await Task.Delay(5000);
        });
        using var client = server.Client(TimeSpan.FromSeconds(30));
        await WarmUpAsync(async () =>
        {
            using var first = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await client.Status.GetAsync(cancellationToken: first.Token);
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Status.GetAsync(cancellationToken: cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// A first call that fails as the measured one will: it also loads and compiles the SDK's
    /// code (seconds on .NET Framework on a busy runner), which no deadline covers, so the time
    /// is measured on the next call.
    /// </summary>
    private static Task WarmUpAsync(Func<Task> call) => Assert.ThrowsAnyAsync<Exception>(call);

    [Fact]
    public async Task Reports_a_closed_connection_as_a_connection_error()
    {
        using var server = new SlowServer(async stream =>
        {
            await SlowServer.WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n{");
        });
        using var client = server.Client(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAnyAsync<ApiConnectionException>(() => client.Status.GetAsync());
        Assert.IsNotType<ApiTimeoutException>(error);
    }
}

/// <summary>A tiny HTTP server on a socket that answers every request with a script.</summary>
internal sealed class SlowServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public SlowServer(Func<NetworkStream, Task> respond)
    {
        _listener.Start();
        Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    using (connection)
                    {
                        try
                        {
                            var stream = connection.GetStream();
                            await ReadHeadersAsync(stream);
                            await respond(stream);
                        }
                        catch (IOException)
                        {
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                });
            }
        });
    }

    public Uri Url { get; }

    public FlashpiClient Client(TimeSpan timeout) =>
        new(new FlashpiClientOptions { ApiKey = Samples.TestKey, BaseUrl = Url, Timeout = timeout, MaxRetries = 0 });

    public static Task WriteAsync(NetworkStream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return stream.WriteAsync(bytes, 0, bytes.Length);
    }

    private static async Task ReadHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[1];
        var seen = new StringBuilder();
        while (!seen.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(buffer, 0, 1) == 0)
            {
                return;
            }
            seen.Append((char)buffer[0]);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TracksAdaptiveTimeoutTests : IAsyncLifetime
{
    HttpListener _listener;
    string _baseUrl;
    CancellationTokenSource _listenCts;
    Task _listenTask;

    int _connectedSeeders;
    long _bytesRead;
    bool _includeFileStats = true;
    int _ffpDelayMs;

    public Task InitializeAsync()
    {
        _connectedSeeders = 0;
        _bytesRead = 0;

        var port = GetFreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_baseUrl + "/");
        _listener.Start();

        _listenCts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoop(_listenCts.Token));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try { _listenCts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _listener?.Close(); } catch { /* ignore */ }
        if (_listenTask != null)
        {
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        }
    }

    static int GetFreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath.TrimEnd('/');

            if (req.HttpMethod == "POST" && path.EndsWith("/torrents", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                if (body.Contains("\"action\":\"get\"", StringComparison.Ordinal) ||
                    body.Contains("\"action\": \"get\"", StringComparison.Ordinal))
                {
                    string fileStats = _includeFileStats
                        ? ""","file_stats":[{"id":1,"path":"a.mkv","length":10}]"""
                        : "";
                    string json =
                        $$"""{"hash":"aabbccddeeff00112233445566778899aabbccdd","stat":3,"category":"jacred","connected_seeders":{{_connectedSeeders}},"bytes_read":{{_bytesRead}}{{fileStats}}}""";
                    await WriteJson(ctx.Response, 200, json);
                    return;
                }

                await WriteJson(ctx.Response, 200, "[]");
                return;
            }

            if (req.HttpMethod == "GET" && path.Contains("/ffp/", StringComparison.OrdinalIgnoreCase))
            {
                if (_ffpDelayMs > 0)
                    await Task.Delay(_ffpDelayMs);

                var bytes = Encoding.UTF8.GetBytes("""{"streams":[{"index":0,"codec_type":"audio"}]}""");
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* ignore */ }
        }
    }

    static async Task WriteJson(HttpListenerResponse resp, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.StatusCode = status;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    const string Hash = "aabbccddeeff00112233445566778899aabbccdd";

    [Fact]
    public async Task WaitDownloadProgress_returns_false_when_no_seeders_and_no_bytes()
    {
        _connectedSeeders = 0;
        _bytesRead = 0;

        var ok = await TracksAnalyzer.WaitDownloadProgress(
            _baseUrl, Hash, CancellationToken.None, progressTimeout: TimeSpan.FromSeconds(3));

        Assert.False(ok);
    }

    [Fact]
    public async Task WaitDownloadProgress_returns_true_when_bytes_read_positive()
    {
        _bytesRead = 4096;

        var ok = await TracksAnalyzer.WaitDownloadProgress(
            _baseUrl, Hash, CancellationToken.None, progressTimeout: TimeSpan.FromSeconds(3));

        Assert.True(ok);
    }

    [Fact]
    public async Task WaitDownloadProgress_returns_true_when_connected_seeders_positive()
    {
        _connectedSeeders = 2;

        var ok = await TracksAnalyzer.WaitDownloadProgress(
            _baseUrl, Hash, CancellationToken.None, progressTimeout: TimeSpan.FromSeconds(3));

        Assert.True(ok);
    }

    [Fact]
    public async Task WaitTorrentReady_without_file_stats_then_no_peers_skips_ffp_path()
    {
        _includeFileStats = false;
        _connectedSeeders = 0;
        _bytesRead = 0;

        var ready = await TracksAnalyzer.WaitTorrentReady(
            _baseUrl, Hash, CancellationToken.None, readyTimeout: TimeSpan.FromSeconds(1));

        Assert.False(ready);

        var canProbe = await TracksAnalyzer.WaitDownloadProgress(
            _baseUrl, Hash, CancellationToken.None, progressTimeout: TimeSpan.FromSeconds(2));

        Assert.False(canProbe);
    }

    [Fact]
    public async Task AnalyzeWithExternalApi_respects_custom_timeout()
    {
        _ffpDelayMs = 5000;
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await TracksAnalyzer.AnalyzeWithExternalApi(
                _baseUrl,
                Hash,
                CancellationToken.None,
                ffpTimeout: TimeSpan.FromMilliseconds(800));
        });

        Assert.InRange(sw.ElapsedMilliseconds, 400, 3000);
    }

    [Fact]
    public async Task AnalyzeWithExternalApi_completes_within_custom_timeout()
    {
        _ffpDelayMs = 100;

        var (result, code) = await TracksAnalyzer.AnalyzeWithExternalApi(
            _baseUrl,
            Hash,
            CancellationToken.None,
            ffpTimeout: TimeSpan.FromSeconds(2));

        Assert.Equal(200, code);
        Assert.NotNull(result);
        Assert.NotNull(result.streams);
        Assert.Single(result.streams);
    }
}

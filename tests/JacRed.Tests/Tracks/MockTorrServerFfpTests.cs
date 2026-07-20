using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

/// <summary>
/// Local HttpListener stand-in for TorrServer /torrents get + /ffp endpoints.
/// </summary>
public class MockTorrServerFfpTests : IAsyncLifetime
{
    HttpListener _listener;
    string _baseUrl;
    CancellationTokenSource _listenCts;
    Task _listenTask;

    int _getCalls;
    bool _readyOnSecondGet;
    int _ffpStatus = 200;
    string _ffpBody = """{"streams":[{"index":0,"codec_type":"audio","codec_name":"aac","tags":{"language":"rus"}}]}""";

    public Task InitializeAsync()
    {
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
                    Interlocked.Increment(ref _getCalls);
                    bool ready = !_readyOnSecondGet || Volatile.Read(ref _getCalls) >= 2;
                    string json = ready
                        ? """{"hash":"aabbccddeeff00112233445566778899aabbccdd","stat":3,"stat_string":"Torrent working","category":"jacred","file_stats":[{"id":1,"path":"a.mkv","length":10}]}"""
                        : """{"hash":"aabbccddeeff00112233445566778899aabbccdd","stat":1,"stat_string":"Torrent getting info","category":"jacred"}""";
                    await WriteJson(ctx.Response, 200, json);
                    return;
                }

                await WriteJson(ctx.Response, 200, "[]");
                return;
            }

            if (req.HttpMethod == "GET" && path.Contains("/ffp/", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = _ffpStatus;
                var bytes = Encoding.UTF8.GetBytes(_ffpBody ?? "");
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

    [Fact]
    public async Task WaitTorrentReady_returns_true_when_file_stats_present()
    {
        _readyOnSecondGet = false;
        var ready = await TracksAnalyzer.WaitTorrentReady(
            _baseUrl,
            "aabbccddeeff00112233445566778899aabbccdd",
            CancellationToken.None,
            readyTimeout: TimeSpan.FromSeconds(5));

        Assert.True(ready);
        Assert.True(Volatile.Read(ref _getCalls) >= 1);
    }

    [Fact]
    public async Task WaitTorrentReady_polls_until_file_stats_appear()
    {
        _readyOnSecondGet = true;

        var ready = await TracksAnalyzer.WaitTorrentReady(
            _baseUrl,
            "aabbccddeeff00112233445566778899aabbccdd",
            CancellationToken.None,
            readyTimeout: TimeSpan.FromSeconds(10));

        Assert.True(ready);
        Assert.True(Volatile.Read(ref _getCalls) >= 2);
    }

    [Fact]
    public async Task AnalyzeWithExternalApi_deserializes_200_streams()
    {
        _ffpStatus = 200;
        var (result, code) = await TracksAnalyzer.AnalyzeWithExternalApi(
            _baseUrl,
            "aabbccddeeff00112233445566778899aabbccdd",
            CancellationToken.None);

        Assert.Equal(200, code);
        Assert.NotNull(result);
        Assert.NotNull(result.streams);
        Assert.Single(result.streams);
        Assert.Equal("rus", result.streams[0].tags.language);
    }

    [Fact]
    public async Task AnalyzeWithExternalApi_returns_status_on_400_without_streams()
    {
        _ffpStatus = 400;
        _ffpBody = "error getting data";

        var (result, code) = await TracksAnalyzer.AnalyzeWithExternalApi(
            _baseUrl,
            "aabbccddeeff00112233445566778899aabbccdd",
            CancellationToken.None);

        Assert.Equal(400, code);
        Assert.Null(result);
        Assert.Equal(1, TracksAnalyzer.NextFailureAttempt(0));
    }
}

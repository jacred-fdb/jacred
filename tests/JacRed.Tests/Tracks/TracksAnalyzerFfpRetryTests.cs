using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Tracks;
using Xunit;

namespace JacRed.Tests.Tracks;

public class TracksAnalyzerFfpRetryTests : IAsyncLifetime
{
    HttpListener _listener;
    string _baseUrl;
    CancellationTokenSource _listenCts;
    Task _listenTask;
    int _ffpCalls;
    int _successOnFileId = 5;

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
            try { ctx = await _listener.GetContextAsync().WaitAsync(token); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url.AbsolutePath;
            if (ctx.Request.HttpMethod == "GET" && path.Contains("/ffp/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _ffpCalls);
                var parts = path.Trim('/').Split('/');
                int fileId = int.Parse(parts[^1]);
                int status = fileId == _successOnFileId ? 200 : 400;
                string body = status == 200
                    ? """{"streams":[{"index":0,"codec_type":"video","codec_name":"h264"}]}"""
                    : "error";
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
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

    [Fact]
    public async Task ProbeFfpWithRetries_succeeds_on_later_file_id()
    {
        _ffpCalls = 0;
        _successOnFileId = 5;
        var fileIds = new[] { 1, 2, 5 };

        var (result, code, err) = await TracksAnalyzer.ProbeFfpWithRetries(
            _baseUrl,
            "aabbccddeeff00112233445566778899aabbccdd",
            fileIds,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Null(err);
        Assert.Equal(200, code);
        Assert.NotNull(result);
        Assert.Single(result.streams);
        Assert.Equal(3, Volatile.Read(ref _ffpCalls));
    }
}

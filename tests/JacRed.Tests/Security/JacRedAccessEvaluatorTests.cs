using System.Net;
using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace JacRed.Tests.Security;

public class JacRedAccessEvaluatorTests
{
    const string ConfigPath = "/api/v1.0/config";
    const string DevPath = "/dev/";

    [Fact]
    public void Spoofed_CF_Connecting_IP_from_public_peer_is_denied_without_devkey()
    {
        WithDevKey(null, () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(
                peerIp: IPAddress.Parse("203.0.113.10"),
                headers: ("CF-Connecting-IP", "127.0.0.1"));

            var result = evaluator.EvaluatePath(ConfigPath, ctx);

            Assert.False(result.IsAllowed);
            Assert.Equal(403, result.DenyStatusCode);
        });
    }

    [Fact]
    public void Loopback_peer_with_public_CF_IP_requires_devkey()
    {
        WithDevKey(null, () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(
                peerIp: IPAddress.Loopback,
                headers: ("CF-Connecting-IP", "8.8.8.8"));

            var result = evaluator.EvaluatePath(ConfigPath, ctx);

            Assert.False(result.IsAllowed);
            Assert.Equal(403, result.DenyStatusCode);
        });
    }

    [Fact]
    public void Direct_loopback_without_headers_is_allowed()
    {
        WithDevKey(null, () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(peerIp: IPAddress.Loopback);

            Assert.True(evaluator.EvaluatePath(ConfigPath, ctx).IsAllowed);
            Assert.True(evaluator.EvaluatePath(DevPath, ctx).IsAllowed);
        });
    }

    [Fact]
    public void Direct_LAN_without_headers_is_allowed()
    {
        WithDevKey(null, () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(peerIp: IPAddress.Parse("10.0.0.5"));

            Assert.True(evaluator.EvaluatePath(ConfigPath, ctx).IsAllowed);
        });
    }

    [Fact]
    public void Empty_devkey_denies_public_peer()
    {
        WithDevKey("", () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(peerIp: IPAddress.Parse("203.0.113.10"));

            var result = evaluator.EvaluatePath(ConfigPath, ctx);

            Assert.False(result.IsAllowed);
            Assert.Equal(403, result.DenyStatusCode);
        });
    }

    [Fact]
    public void Valid_X_Dev_Key_allows_public_peer()
    {
        WithDevKey("secret-dev-key", () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(
                peerIp: IPAddress.Parse("203.0.113.10"),
                headers: ("X-Dev-Key", "secret-dev-key"));

            Assert.True(evaluator.EvaluatePath(ConfigPath, ctx).IsAllowed);
            Assert.True(evaluator.EvaluatePath(DevPath, ctx).IsAllowed);
        });
    }

    [Fact]
    public void Invalid_X_Dev_Key_denies_public_peer_with_401()
    {
        WithDevKey("secret-dev-key", () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(
                peerIp: IPAddress.Parse("203.0.113.10"),
                headers: ("X-Dev-Key", "wrong"));

            var result = evaluator.EvaluatePath(ConfigPath, ctx);

            Assert.False(result.IsAllowed);
            Assert.Equal(401, result.DenyStatusCode);
        });
    }

    [Fact]
    public void Loopback_proxy_with_valid_devkey_allows_public_client()
    {
        WithDevKey("secret-dev-key", () =>
        {
            var evaluator = CreateEvaluator();
            var ctx = CreateContext(
                peerIp: IPAddress.Loopback,
                ("CF-Connecting-IP", "8.8.8.8"),
                ("X-Dev-Key", "secret-dev-key"));

            Assert.True(evaluator.EvaluatePath(ConfigPath, ctx).IsAllowed);
        });
    }

    static JacRedAccessEvaluator CreateEvaluator()
        => new(new JacRedApiKeyValidator(), new JacRedDevKeyValidator());

    static DefaultHttpContext CreateContext(IPAddress peerIp, params (string Name, string Value)[] headers)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = peerIp;
        ctx.Request.Method = "GET";
        foreach (var (name, value) in headers)
            ctx.Request.Headers[name] = value;
        ClientNetworkContext.CaptureOriginalRemoteIp(ctx);
        return ctx;
    }

    static void WithDevKey(string value, Action action)
    {
        _ = AppInit.conf; // ensure provider initialized
        var previous = AppInit.conf.devkey;
        try
        {
            AppInit.conf.devkey = value;
            action();
        }
        finally
        {
            AppInit.conf.devkey = previous;
        }
    }
}

using System.Net;
using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace JacRed.Tests.Security;

public class ClientNetworkContextTests
{
    [Fact]
    public void Public_peer_ignores_spoofed_CF_Connecting_IP()
    {
        var ctx = CreateContext(
            peerIp: IPAddress.Parse("203.0.113.10"),
            headers: ("CF-Connecting-IP", "127.0.0.1"));

        var network = ClientNetworkContext.From(ctx);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), network.ClientIp);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), network.PeerIp);
        Assert.False(network.IsDirectLocalClient);
        Assert.False(network.IsSameHostReverseProxy);
    }

    [Fact]
    public void Public_peer_ignores_spoofed_X_Real_IP()
    {
        var ctx = CreateContext(
            peerIp: IPAddress.Parse("203.0.113.10"),
            headers: ("X-Real-IP", "192.168.1.1"));

        var network = ClientNetworkContext.From(ctx);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), network.ClientIp);
        Assert.False(network.IsDirectLocalClient);
    }

    [Fact]
    public void Loopback_peer_trusts_CF_Connecting_IP()
    {
        var ctx = CreateContext(
            peerIp: IPAddress.Loopback,
            headers: ("CF-Connecting-IP", "8.8.8.8"));

        var network = ClientNetworkContext.From(ctx);

        Assert.Equal(IPAddress.Parse("8.8.8.8"), network.ClientIp);
        Assert.Equal(IPAddress.Loopback, network.PeerIp);
        Assert.False(network.IsDirectLocalClient);
        Assert.True(network.IsSameHostReverseProxy);
    }

    [Fact]
    public void Direct_loopback_without_headers_is_local_client()
    {
        var ctx = CreateContext(peerIp: IPAddress.Loopback);

        var network = ClientNetworkContext.From(ctx);

        Assert.Equal(IPAddress.Loopback, network.ClientIp);
        Assert.True(network.IsDirectLocalClient);
        Assert.True(network.IsSameHostReverseProxy);
    }

    [Fact]
    public void Direct_LAN_without_headers_is_local_client()
    {
        var ctx = CreateContext(peerIp: IPAddress.Parse("192.168.1.50"));

        var network = ClientNetworkContext.From(ctx);

        Assert.Equal(IPAddress.Parse("192.168.1.50"), network.ClientIp);
        Assert.True(network.IsDirectLocalClient);
        Assert.False(network.IsSameHostReverseProxy);
    }

    static DefaultHttpContext CreateContext(IPAddress peerIp, params (string Name, string Value)[] headers)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = peerIp;
        foreach (var (name, value) in headers)
            ctx.Request.Headers[name] = value;
        ClientNetworkContext.CaptureOriginalRemoteIp(ctx);
        return ctx;
    }
}

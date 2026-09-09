using System.Net;
using System.Net.Http;
using JacRed.Models.AppConf;
using Xunit;
using JacHttp = JacRed.Infrastructure.Networking.HttpClient;

namespace JacRed.Tests.Networking;

public class ProxyHandlerTests
{
    [Theory]
    [InlineData("socks5://10.0.0.2:1080", "socks5")]
    [InlineData("socks5h://127.0.0.1:9050", "socks5")]
    [InlineData("socks://127.0.0.1:1080", "socks5")]
    [InlineData("http://10.0.0.1:8080", "http")]
    [InlineData("10.0.0.1:8080", "http")]
    public void NormalizeProxyAddress_keeps_or_maps_scheme(string raw, string scheme)
    {
        Uri uri = JacHttp.NormalizeProxyAddress(raw);
        Assert.Equal(scheme, uri.Scheme);
    }

    [Fact]
    public void CreateWebProxy_preserves_socks5_and_auth()
    {
        var settings = new ProxySettings
        {
            useAuth = true,
            username = "user",
            password = "secret",
            BypassOnLocal = true
        };

        WebProxy proxy = JacHttp.CreateWebProxy("socks5://10.0.0.2:1080", settings);

        Assert.Equal("socks5", proxy.Address.Scheme);
        Assert.Equal(1080, proxy.Address.Port);
        Assert.True(proxy.BypassProxyOnLocal);
        Assert.NotNull(proxy.Credentials);
        var creds = proxy.Credentials.GetCredential(proxy.Address, "socks");
        Assert.Equal("user", creds.UserName);
        Assert.Equal("secret", creds.Password);
    }

    [Fact]
    public void CreateHandler_uses_sockets_handler_for_socks5()
    {
        WebProxy proxy = JacHttp.CreateWebProxy("socks5://127.0.0.1:1080", null);

        using SocketsHttpHandler handler = JacHttp.CreateHandler(proxy, DecompressionMethods.GZip);

        Assert.True(handler.UseProxy);
        Assert.Same(proxy, handler.Proxy);
        Assert.True(handler.AllowAutoRedirect);
    }
}

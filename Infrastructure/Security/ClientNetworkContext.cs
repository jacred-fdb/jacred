using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Sockets;

namespace JacRed.Infrastructure.Security
{
    public interface IClientNetworkContext
    {
        IPAddress ClientIp { get; }
        IPAddress PeerIp { get; }
        bool IsDirectLocalClient { get; }
        bool IsViaLocalPeer { get; }
        bool IsTrustedContext { get; }
    }

    /// <summary>
    /// Client IP (after X-Forwarded-For) vs peer IP (direct TCP to Kestrel).
    /// See temp/SecurityAnalysis.md and Startup UseForwardedHeaders.
    /// </summary>
    public sealed class ClientNetworkContext : IClientNetworkContext
    {
        public const string OriginalRemoteIpItemKey = "JacRed.OriginalRemoteIp";

        public IPAddress ClientIp { get; }
        public IPAddress PeerIp { get; }
        public bool IsDirectLocalClient { get; }
        public bool IsViaLocalPeer { get; }
        public bool IsTrustedContext { get; }

        ClientNetworkContext(IPAddress clientIp, IPAddress peerIp)
        {
            ClientIp = clientIp;
            PeerIp = peerIp;
            IsDirectLocalClient = IsLocalOrPrivate(clientIp);
            IsViaLocalPeer = IsLocalOrPrivate(peerIp);
            IsTrustedContext = IsDirectLocalClient || IsViaLocalPeer;
        }

        public static void CaptureOriginalRemoteIp(HttpContext httpContext)
        {
            httpContext.Items[OriginalRemoteIpItemKey] = httpContext.Connection.RemoteIpAddress;
        }

        public static IClientNetworkContext From(HttpContext httpContext)
        {
            httpContext.Items.TryGetValue(OriginalRemoteIpItemKey, out var peerValue);
            var peerIp = peerValue as IPAddress;
            return new ClientNetworkContext(httpContext.Connection.RemoteIpAddress, peerIp);
        }

        public static bool IsLocalOrPrivate(IPAddress remoteIp)
        {
            if (remoteIp == null) return false;
            if (remoteIp.IsIPv4MappedToIPv6)
                return IsLocalOrPrivate(remoteIp.MapToIPv4());
            var bytes = remoteIp.GetAddressBytes();
            if (remoteIp.AddressFamily == AddressFamily.InterNetwork)
            {
                if (bytes[0] == 127) return true;
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                return false;
            }
            if (remoteIp.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IPv6Loopback.Equals(remoteIp)) return true;
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
                if ((bytes[0] & 0xfe) == 0xfc) return true;
                return false;
            }
            return false;
        }
    }
}

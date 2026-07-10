using Microsoft.AspNetCore.Http;
using System;
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
        /// <summary>cloudflared/nginx on 127.0.0.1 — same-host reverse proxy, not a direct LAN client.</summary>
        bool IsSameHostReverseProxy { get; }
        bool IsTrustedContext { get; }
    }

    /// <summary>
    /// Client IP (after X-Forwarded-For / CF-Connecting-IP) vs peer IP (direct TCP to Kestrel).
    /// Used with Startup UseForwardedHeaders for reverse-proxy deployments.
    /// </summary>
    public sealed class ClientNetworkContext : IClientNetworkContext
    {
        public const string OriginalRemoteIpItemKey = "JacRed.OriginalRemoteIp";

        public IPAddress ClientIp { get; }
        public IPAddress PeerIp { get; }
        public bool IsDirectLocalClient { get; }
        public bool IsViaLocalPeer { get; }
        public bool IsSameHostReverseProxy { get; }
        public bool IsTrustedContext { get; }

        ClientNetworkContext(IPAddress clientIp, IPAddress peerIp)
        {
            ClientIp = clientIp;
            PeerIp = peerIp;
            IsDirectLocalClient = IsLocalOrPrivate(clientIp);
            IsViaLocalPeer = IsLocalOrPrivate(peerIp);
            IsSameHostReverseProxy = IsLoopback(peerIp);
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
            var clientIp = ResolveClientIp(httpContext);
            return new ClientNetworkContext(clientIp, peerIp);
        }

        /// <summary>
        /// Prefer proxy client-identity headers (Cloudflare Tunnel sends CF-Connecting-IP, not always XFF).
        /// UseForwardedHeaders already applies X-Forwarded-For to Connection.RemoteIpAddress.
        /// </summary>
        static IPAddress ResolveClientIp(HttpContext httpContext)
        {
            if (TryParseHeaderIp(httpContext.Request.Headers, "CF-Connecting-IP", out var cfIp))
                return cfIp;
            if (TryParseHeaderIp(httpContext.Request.Headers, "X-Real-IP", out var realIp))
                return realIp;
            return httpContext.Connection.RemoteIpAddress;
        }

        /// <summary>Headers that indicate the request was forwarded by a reverse proxy / tunnel.</summary>
        public static bool HasProxyClientIdentityHeaders(HttpContext httpContext)
        {
            var headers = httpContext.Request.Headers;
            if (headers.ContainsKey("CF-Connecting-IP") || headers.ContainsKey("CF-Ray"))
                return true;
            if (headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("X-Real-IP"))
                return true;
            if (headers.TryGetValue("X-Forwarded-Proto", out var proto)
                && !string.Equals(proto.ToString(), "http", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        static bool TryParseHeaderIp(IHeaderDictionary headers, string name, out IPAddress ip)
        {
            ip = null;
            if (!headers.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                return false;
            var raw = value.ToString().Split(',')[0].Trim();
            return IPAddress.TryParse(raw, out ip);
        }

        public static bool IsLoopback(IPAddress ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6)
                return IsLoopback(ip.MapToIPv4());
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.GetAddressBytes()[0] == 127;
            return IPAddress.IPv6Loopback.Equals(ip);
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

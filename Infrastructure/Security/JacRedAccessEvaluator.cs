using Microsoft.AspNetCore.Http;

namespace JacRed.Infrastructure.Security
{
    public interface IJacRedAccessEvaluator
    {
        JacRedAccessResult EvaluatePath(string path, HttpContext httpContext);
        bool ShouldSetPrivateNetworkHeader(IClientNetworkContext network, string path);
    }

    public sealed class JacRedAccessEvaluator : IJacRedAccessEvaluator
    {
        readonly IJacRedApiKeyValidator _apiKeyValidator;
        readonly IJacRedDevKeyValidator _devKeyValidator;

        public JacRedAccessEvaluator(IJacRedApiKeyValidator apiKeyValidator, IJacRedDevKeyValidator devKeyValidator)
        {
            _apiKeyValidator = apiKeyValidator;
            _devKeyValidator = devKeyValidator;
        }

        public JacRedAccessResult EvaluatePath(string path, HttpContext httpContext)
        {
            var policy = JacRedEndpointRegistry.ResolvePolicy(path);
            var network = ClientNetworkContext.From(httpContext);
            var method = httpContext.Request.Method;

            if (policy == JacRedAccessPolicy.DevAdmin)
                return EvaluateDevAdmin(network, httpContext, method);

            if (policy == JacRedAccessPolicy.ConfigApi)
                return EvaluateConfigApi(network, httpContext, method);

            if (policy == JacRedAccessPolicy.ApiKeyWhenConfigured && _apiKeyValidator.IsConfigured)
            {
                if (_apiKeyValidator.Validate(httpContext))
                    return JacRedAccessResult.Allow;

                return JacRedAccessResult.Deny(
                    DenyStatus(keyConfigured: true, method),
                    setPrivateNetworkHeader: ShouldSetPrivateNetworkHeader(network, path));
            }

            return JacRedAccessResult.Allow;
        }

        public bool ShouldSetPrivateNetworkHeader(IClientNetworkContext network, string path)
            => network.IsTrustedContext || !JacRedEndpointRegistry.IsRestrictedAdminPath(path);

        JacRedAccessResult EvaluateDevAdmin(IClientNetworkContext network, HttpContext httpContext, string method)
        {
            if (IsDevEndpointAccessAllowed(network, httpContext))
                return JacRedAccessResult.Allow;
            return JacRedAccessResult.Deny(DenyStatus(_devKeyValidator.IsConfigured, method));
        }

        JacRedAccessResult EvaluateConfigApi(IClientNetworkContext network, HttpContext httpContext, string method)
            => EvaluateDevAdmin(network, httpContext, method);

        static bool IsDevEndpointAccessAllowed(IClientNetworkContext network, HttpContext httpContext)
        {
            if (IsTrustedLanClient(network, httpContext))
                return true;
            // Empty/missing devkey = LAN-only (docs/example.conf); do not treat as world-open.
            if (string.IsNullOrEmpty(AppInit.conf?.devkey))
                return false;
            return JacRedKeyUtils.DevKeyMatches(httpContext, AppInit.conf.devkey);
        }

        /// <summary>
        /// LAN / direct localhost. Reverse proxy (loopback or Docker/LAN peer) alone is not enough.
        /// </summary>
        static bool IsTrustedLanClient(IClientNetworkContext network, HttpContext httpContext)
        {
            if (!network.IsDirectLocalClient)
                return false;

            // Reverse proxy on loopback OR Docker/LAN (e.g. Traefik peer 172.x): ClientIp stays the
            // proxy private IP and looks like LAN. Proxy identity headers mean the real client is
            // behind the proxy — require DEV key. Do not trust header values for granting LAN access.
            if (network.IsViaLocalPeer && ClientNetworkContext.HasProxyClientIdentityHeaders(httpContext))
                return false;

            return true;
        }

        public static int DenyStatus(bool keyConfigured, string method)
            => method == "OPTIONS" ? 204 : (keyConfigured ? 401 : 403);
    }
}

using JacRed.Models.AppConf;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Networking
{
    public static class HttpClient
    {
        static string useragent => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

        #region webProxy
        static ConcurrentBag<string> proxyRandomList = new ConcurrentBag<string>();

        public static WebProxy webProxy()
        {
            if (proxyRandomList.Count == 0)
            {
                foreach (string ip in AppInit.conf.proxy.list.OrderBy(a => Guid.NewGuid()))
                    proxyRandomList.Add(ip);
            }

            proxyRandomList.TryTake(out string proxyip);

            ICredentials credentials = null;

            if (AppInit.conf.proxy.useAuth)
                credentials = new NetworkCredential(AppInit.conf.proxy.username, AppInit.conf.proxy.password);

            return new WebProxy(proxyip, AppInit.conf.proxy.BypassOnLocal, null, credentials);
        }

        static WebProxy CreateWebProxy(string proxyUrl, ProxySettings settings)
        {
            ICredentials credentials = null;

            if (settings != null && settings.useAuth)
                credentials = new NetworkCredential(settings.username, settings.password);

            return new WebProxy(proxyUrl, settings?.BypassOnLocal ?? false, null, credentials);
        }

        static List<WebProxy> ResolveProxies(string url, bool useproxy, WebProxy proxyOverride)
        {
            if (proxyOverride != null)
                return new List<WebProxy> { proxyOverride };

            if (AppInit.conf.globalproxy != null)
            {
                foreach (var p in AppInit.conf.globalproxy)
                {
                    if (p.list == null || p.list.Count == 0)
                        continue;

                    if (!Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                        continue;

                    return p.list
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(_ => Guid.NewGuid())
                        .Select(u => CreateWebProxy(u, p))
                        .ToList();
                }
            }

            if (AppInit.conf.proxy.list != null && AppInit.conf.proxy.list.Count > 0 && useproxy)
                return new List<WebProxy> { webProxy() };

            return new List<WebProxy>();
        }

        static HttpClientHandler CreateHandler(WebProxy proxy, DecompressionMethods decompression)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = decompression
            };

            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }

            return handler;
        }
        #endregion


        #region Get
        async public static ValueTask<string> Get(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, long MaxResponseContentBufferSize = 0, bool useproxy = false, WebProxy proxy = null, int httpversion = 1)
        {
            return (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, MaxResponseContentBufferSize: MaxResponseContentBufferSize, useproxy: useproxy, proxy: proxy, httpversion: httpversion)).content;
        }
        #endregion

        #region Get<T>
        async public static ValueTask<T> Get<T>(string url, Encoding encoding = default, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool IgnoreDeserializeObject = false, bool useproxy = false, WebProxy proxy = null)
        {
            try
            {
                string html = (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, proxy: proxy)).content;
                if (html == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(html, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(html);
            }
            catch
            {
                return default;
            }
        }
        #endregion

        #region BaseGetAsync
        async public static ValueTask<(string content, HttpResponseMessage response)> BaseGetAsync(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null, int httpversion = 1)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            foreach (var px in proxies)
            {
                try
                {
                    using (var handler = CreateHandler(px, DecompressionMethods.GZip | DecompressionMethods.Deflate))
                    using (var client = new System.Net.Http.HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                        client.MaxResponseContentBufferSize = MaxResponseContentBufferSize == 0 ? 10_000_000 : MaxResponseContentBufferSize; // 10MB
                        client.DefaultRequestHeaders.Add("user-agent", useragent);

                        if (cookie != null)
                            client.DefaultRequestHeaders.Add("cookie", cookie);

                        if (referer != null)
                            client.DefaultRequestHeaders.Add("referer", referer);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                client.DefaultRequestHeaders.Add(item.name, item.val);
                        }

                        var req = new HttpRequestMessage(HttpMethod.Get, url)
                        {
                            Version = new Version(httpversion, 0)
                        };

                        using (HttpResponseMessage response = await client.SendAsync(req))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                continue;

                            using (HttpContent content = response.Content)
                            {
                                if (encoding != default)
                                {
                                    string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return (res, response);
                                }
                                else
                                {
                                    string res = await content.ReadAsStringAsync();
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return (res, response);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return (null, new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.InternalServerError,
                RequestMessage = new HttpRequestMessage()
            });
        }
        #endregion


        #region Post
        public static ValueTask<string> Post(string url, string data, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            return Post(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, proxy: proxy);
        }

        async public static ValueTask<string> Post(string url, HttpContent data, Encoding encoding = default, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            foreach (var px in proxies)
            {
                try
                {
                    using (var handler = CreateHandler(px, DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate))
                    using (var client = new System.Net.Http.HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                        client.MaxResponseContentBufferSize = MaxResponseContentBufferSize != 0 ? MaxResponseContentBufferSize : 10_000_000; // 10MB

                        client.DefaultRequestHeaders.Add("user-agent", useragent);
                        if (cookie != null)
                            client.DefaultRequestHeaders.Add("cookie", cookie);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                client.DefaultRequestHeaders.Add(item.name, item.val);
                        }

                        using (HttpResponseMessage response = await client.PostAsync(url, data))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                continue;

                            using (HttpContent content = response.Content)
                            {
                                if (encoding != default)
                                {
                                    string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return res;
                                }
                                else
                                {
                                    string res = await content.ReadAsStringAsync();
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return res;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
        #endregion

        #region Post<T>
        async public static ValueTask<T> Post<T>(string url, string data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            return await Post<T>(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, encoding: encoding, proxy: proxy, IgnoreDeserializeObject: IgnoreDeserializeObject);
        }

        async public static ValueTask<T> Post<T>(string url, HttpContent data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            try
            {
                string json = await Post(url, data, cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, encoding: encoding, proxy: proxy);
                if (json == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
        }
        #endregion


        #region Download
        async public static ValueTask<byte[]> Download(string url, string cookie = null, string referer = null, int timeoutSeconds = 30, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            foreach (var px in proxies)
            {
                try
                {
                    var handler = CreateHandler(px, DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate);
                    handler.AllowAutoRedirect = true;

                    using (handler)
                    using (var client = new System.Net.Http.HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                        client.MaxResponseContentBufferSize = MaxResponseContentBufferSize == 0 ? 10_000_000 : MaxResponseContentBufferSize; // 10MB
                        client.DefaultRequestHeaders.Add("user-agent", useragent);

                        if (cookie != null)
                            client.DefaultRequestHeaders.Add("cookie", cookie);

                        if (referer != null)
                            client.DefaultRequestHeaders.Add("referer", referer);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                client.DefaultRequestHeaders.Add(item.name, item.val);
                        }

                        using (HttpResponseMessage response = await client.GetAsync(url))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                continue;

                            using (HttpContent content = response.Content)
                            {
                                byte[] res = await content.ReadAsByteArrayAsync();
                                if (res.Length == 0)
                                    continue;

                                return res;
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
        #endregion
    }
}

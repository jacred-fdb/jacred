using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Http = JacRed.Infrastructure.Networking.HttpClient;

namespace JacRed.Infrastructure.Trackers.Anifilm
{
    public class AnifilmSyncService
    {
        const string TrackerName = AnifilmParser.TrackerName;
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static readonly Regex CsrfInputRe = new(
            @"<input[^>]+name=""([^""]*CSRF[^""]*)""[^>]+value=""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex CsrfInputRe2 = new(
            @"<input[^>]+value=""([^""]+)""[^>]+name=""([^""]*CSRF[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        readonly object _cookieLock = new object();
        string _dynCookie;
        DateTime _lastLoginAttempt = DateTime.MinValue;

        /// <summary>
        /// Parse category listings. fullparse uses larger per-category page limits (Go-compatible).
        /// </summary>
        public async Task<string> ParseAsync(bool fullparse = false)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string host = AppInit.conf.Anifilm?.rqHost()?.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(host))
                {
                    ParserLog.Write(TrackerName, "Config missing — add Anifilm.host");
                    return "config missing";
                }

                try
                {
                    var sw = Stopwatch.StartNew();
                    int totalFetched = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                    await EnsureLoginAsync();

                    ParserLog.Write(TrackerName, "Starting parse", new Dictionary<string, object>
                    {
                        { "fullparse", fullparse },
                        { "host", host }
                    });

                    foreach (var kv in AnifilmCategories.Map)
                    {
                        string cat = kv.Key;
                        var meta = kv.Value;
                        int maxPage = AnifilmCategories.MaxPages(meta, fullparse);
                        for (int page = 1; page <= maxPage; page++)
                        {
                            if (page > 1 && AppInit.conf.Anifilm.parseDelay > 0)
                                await Task.Delay(AppInit.conf.Anifilm.parseDelay);

                            DateTime createTime = DateTime.UtcNow;
                            if (fullparse)
                                createTime = DateTime.UtcNow.AddDays(-(2 * page));

                            string pageUrl = $"{host}/releases/page/{page}?category={cat}";
                            string body = await Http.Get(
                                pageUrl,
                                encoding: Encoding.UTF8,
                                cookie: CookieHeader(),
                                referer: host + "/",
                                useproxy: AppInit.conf.Anifilm.useproxy);

                            if (string.IsNullOrEmpty(body) || LooksLikeLoginForm(body))
                            {
                                if (LooksLikeLoginForm(body))
                                    InvalidateCookie();
                                continue;
                            }

                            var items = AnifilmParser.ParseListingHtml(body, host, meta.Types, createTime);
                            totalFetched += items.Count;
                            if (items.Count == 0)
                                continue;

                            var (added, updated, skipped, failed) = await SaveTorrentsAsync(items, host);
                            totalAdded += added;
                            totalUpdated += updated;
                            totalSkipped += skipped;
                            totalFailed += failed;

                            ParserLog.Write(TrackerName, "Category page done", new Dictionary<string, object>
                            {
                                { "cat", cat },
                                { "page", page },
                                { "maxPage", maxPage },
                                { "fetched", items.Count },
                                { "added", added },
                                { "skipped", skipped },
                                { "failed", failed }
                            });
                        }
                    }

                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                        new Dictionary<string, object>
                        {
                            { "fetched", totalFetched },
                            { "added", totalAdded },
                            { "updated", totalUpdated },
                            { "skipped", totalSkipped },
                            { "failed", totalFailed }
                        });
                }
                catch (OperationCanceledException oce)
                {
                    ParserLog.Write(TrackerName, "Canceled", new Dictionary<string, object>
                    {
                        { "message", oce.Message },
                        { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                    return "canceled";
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException)
                        throw;

                    ParserLog.Write(TrackerName, "Error", new Dictionary<string, object>
                    {
                        { "message", ex.Message },
                        { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                    });
                }

                return "ok";
            });
        }

        async Task<(int added, int updated, int skipped, int failed)> SaveTorrentsAsync(
            List<AnifilmDetails> torrents, string host)
        {
            int added = 0, updated = 0, skipped = 0, failed = 0;
            if (torrents == null || torrents.Count == 0)
                return (0, 0, 0, 0);

            await FileDB.AddOrUpdate(torrents, async (torrent, db) =>
            {
                var t = (AnifilmDetails)torrent;
                bool exists = db.TryGetValue(t.url, out TorrentDetails cached);

                bool needMagnet = !exists || string.IsNullOrWhiteSpace(cached.magnet);
                if (!needMagnet)
                {
                    string existTitle = (cached.title ?? "").Replace(" [1080p]", "", StringComparison.Ordinal);
                    if (!string.Equals(existTitle, t.title, StringComparison.Ordinal))
                        needMagnet = true;
                }

                if (!needMagnet)
                {
                    skipped++;
                    ParserLog.WriteSkipped(TrackerName, cached, "no changes");
                    return false;
                }

                string detailHtml = await Http.Get(
                    t.url,
                    encoding: Encoding.UTF8,
                    cookie: CookieHeader(),
                    referer: host + "/",
                    useproxy: AppInit.conf.Anifilm.useproxy);

                if (string.IsNullOrEmpty(detailHtml) || LooksLikeLoginForm(detailHtml))
                {
                    if (LooksLikeLoginForm(detailHtml))
                        InvalidateCookie();
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "detail page empty or login form");
                    return false;
                }

                var (tid, is1080p) = AnifilmParser.ExtractTorrentDownloadPath(detailHtml);
                if (string.IsNullOrWhiteSpace(tid))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "tid not found");
                    return false;
                }

                if (is1080p && !(t.title ?? "").Contains(" [1080p]", StringComparison.Ordinal))
                    t.title += " [1080p]";

                t.downloadId = tid;
                string downUrl = host + "/" + tid.TrimStart('/');
                byte[] torrentFile = await Http.Download(
                    downUrl,
                    cookie: CookieHeader(),
                    referer: t.url,
                    useproxy: AppInit.conf.Anifilm.useproxy);

                if (torrentFile != null && torrentFile.Length > 0)
                {
                    string magnet = BencodeTo.Magnet(torrentFile);
                    if (!string.IsNullOrWhiteSpace(magnet))
                    {
                        t.magnet = magnet;
                        string sizeName = BencodeTo.SizeName(torrentFile);
                        if (!string.IsNullOrWhiteSpace(sizeName))
                            t.sizeName = sizeName;
                    }
                }

                if (string.IsNullOrWhiteSpace(t.magnet))
                {
                    failed++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet");
                    return false;
                }

                if (exists)
                {
                    updated++;
                    ParserLog.WriteUpdated(TrackerName, t, "magnet/title updated");
                }
                else
                {
                    added++;
                    ParserLog.WriteAdded(TrackerName, t);
                }

                return true;
            });

            return (added, updated, skipped, failed);
        }

        string CookieHeader()
        {
            lock (_cookieLock)
            {
                if (!string.IsNullOrWhiteSpace(_dynCookie))
                    return _dynCookie;
            }

            return string.IsNullOrWhiteSpace(AppInit.conf.Anifilm?.cookie)
                ? null
                : AppInit.conf.Anifilm.cookie.Trim();
        }

        async Task EnsureLoginAsync()
        {
            if (!string.IsNullOrWhiteSpace(CookieHeader()))
                return;
            if (string.IsNullOrWhiteSpace(AppInit.conf.Anifilm?.login?.u))
                return;

            await TakeLoginAsync();
        }

        void InvalidateCookie()
        {
            lock (_cookieLock)
            {
                _dynCookie = null;
                _lastLoginAttempt = DateTime.MinValue;
            }
        }

        async Task TakeLoginAsync()
        {
            lock (_cookieLock)
            {
                if (DateTime.UtcNow - _lastLoginAttempt < TimeSpan.FromMinutes(2))
                    return;
                _lastLoginAttempt = DateTime.UtcNow;
            }

            string host = AppInit.conf.Anifilm?.rqHost()?.TrimEnd('/');
            string user = AppInit.conf.Anifilm?.login?.u?.Trim();
            string pass = AppInit.conf.Anifilm?.login?.p ?? "";
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
                return;

            ParserLog.Write(TrackerName, "Attempting login", new Dictionary<string, object>
            {
                { "host", host },
                { "user", user }
            });

            try
            {
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
                };
                using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

                string loginUrl = host + "/account/login";
                using var getReq = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                getReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var getResp = await client.SendAsync(getReq);
                string pageHtml = await getResp.Content.ReadAsStringAsync();

                string allCookies = MergeSetCookie("", getResp.Headers);
                string csrfName = null, csrfToken = null;
                var m1 = CsrfInputRe.Match(pageHtml);
                if (m1.Success)
                {
                    csrfName = m1.Groups[1].Value;
                    csrfToken = WebUtility.HtmlDecode(m1.Groups[2].Value);
                }
                else
                {
                    var m2 = CsrfInputRe2.Match(pageHtml);
                    if (m2.Success)
                    {
                        csrfToken = WebUtility.HtmlDecode(m2.Groups[1].Value);
                        csrfName = m2.Groups[2].Value;
                    }
                }

                if (string.IsNullOrWhiteSpace(csrfToken) || string.IsNullOrWhiteSpace(csrfName))
                {
                    ParserLog.Write(TrackerName, "Login failed — CSRF token not found");
                    return;
                }

                var form = new Dictionary<string, string>
                {
                    [csrfName] = csrfToken,
                    ["LoginForm[username]"] = user,
                    ["LoginForm[password]"] = pass,
                    ["LoginForm[pass]"] = ""
                };

                using var postContent = new FormUrlEncodedContent(form);
                using var postReq = new HttpRequestMessage(HttpMethod.Post, loginUrl) { Content = postContent };
                postReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                postReq.Headers.TryAddWithoutValidation("Referer", loginUrl);
                if (!string.IsNullOrWhiteSpace(allCookies))
                    postReq.Headers.TryAddWithoutValidation("Cookie", allCookies);

                using var postResp = await client.SendAsync(postReq);
                string finalCookies = MergeSetCookie(allCookies, postResp.Headers);
                int status = (int)postResp.StatusCode;
                if (status != 302 && (status < 200 || status >= 300))
                {
                    ParserLog.Write(TrackerName, "Login failed", new Dictionary<string, object> { { "status", status } });
                    return;
                }

                if (string.IsNullOrWhiteSpace(finalCookies))
                {
                    ParserLog.Write(TrackerName, "Login failed — no cookies in response");
                    return;
                }

                lock (_cookieLock)
                    _dynCookie = finalCookies;

                ParserLog.Write(TrackerName, "Login OK");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ParserLog.Write(TrackerName, "Login error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
            }
        }

        static bool LooksLikeLoginForm(string body) =>
            !string.IsNullOrEmpty(body) &&
            (body.Contains("action=\"/account/login\"", StringComparison.OrdinalIgnoreCase)
             || body.Contains("action='/account/login'", StringComparison.OrdinalIgnoreCase));

        static string MergeSetCookie(string existing, System.Net.Http.Headers.HttpResponseHeaders headers)
        {
            string result = existing ?? "";
            if (headers == null || !headers.TryGetValues("Set-Cookie", out var lines))
                return result;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string part = line.Split(';', 2)[0].Trim();
                if (part.Length == 0)
                    continue;
                result = MergeCookieStrings(result, part);
            }

            return result;
        }

        static string MergeCookieStrings(string a, string b)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return;
                foreach (string piece in s.Split(';'))
                {
                    string p = piece.Trim();
                    if (p.Length == 0)
                        continue;
                    int eq = p.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    map[p[..eq].Trim()] = p[(eq + 1)..].Trim();
                }
            }

            Add(a);
            Add(b);
            return string.Join("; ", map.Select(kv => kv.Key + "=" + kv.Value));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Trackers.AnimeLayer
{
    public class AnimeLayerSyncService
    {
        const string TrackerName = "animelayer";
        const string CookieCacheKey = "animelayer:cookie";

        static readonly SemaphoreSlim loginSemaphore = new SemaphoreSlim(1, 1);

        static volatile bool workParse;
        static readonly object workParseLock = new object();

        readonly IMemoryCache _memoryCache;

        public AnimeLayerSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// Ensures the host URL uses HTTPS. Automatically converts HTTP to HTTPS.
        /// Assumes hosts support both HTTP and HTTPS protocols.
        /// </summary>
        static string EnsureHttps(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return host;

            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "https://" + host.Substring(7);

            if (!host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "https://" + host;

            return host;
        }

        string Cookie()
        {
            if (_memoryCache.TryGetValue(CookieCacheKey, out string cookie))
                return cookie;

            return null;
        }

        void InvalidateCookie()
        {
            _memoryCache.Remove(CookieCacheKey);
            ParserLog.Write(TrackerName, "Cookie invalidated", new Dictionary<string, object>
            {
                { "reason", "likely expired during parsing" }
            });
        }

        async Task<bool> ValidateCookie(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
                return false;

            try
            {
                string baseHost = EnsureHttps(AppInit.conf.Animelayer.host);
                string testUrl = $"{baseHost}/torrents/anime/";
                string html = await HttpClient.Get(testUrl, cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);

                if (html == null)
                {
                    ParserLog.Write(TrackerName, "Cookie validation failed", new Dictionary<string, object>
                    {
                        { "reason", "null response" }
                    });
                    return false;
                }

                bool isValid = html.Contains("id=\"wrapper\"") && !html.Contains("id=\"loginForm\"") && !html.Contains("/auth/login/");

                ParserLog.Write(TrackerName, "Cookie validation", new Dictionary<string, object>
                {
                    { "isValid", isValid },
                    { "hasWrapper", html.Contains("id=\"wrapper\"") },
                    { "hasLoginForm", html.Contains("id=\"loginForm\"") || html.Contains("/auth/login/") }
                });

                return isValid;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                ParserLog.Write(TrackerName, "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                return false;
            }
            catch (UriFormatException ex)
            {
                ParserLog.Write(TrackerName, "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                return false;
            }
            catch (Exception ex)
            {
                ParserLog.Write(TrackerName, "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                throw;
            }
        }

        public async Task<bool> TakeLoginAsync()
        {
            if (!await loginSemaphore.WaitAsync(0))
            {
                ParserLog.Write(TrackerName, "TakeLogin skipped", new Dictionary<string, object>
                {
                    { "reason", "login already in progress" }
                });
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) ||
                    string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
                {
                    ParserLog.Write(TrackerName, "TakeLogin failed", new Dictionary<string, object>
                    {
                        { "reason", "credentials not configured" }
                    });
                    return false;
                }

                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.MaxResponseContentBufferSize = 2000000;
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    client.DefaultRequestHeaders.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                    client.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.5");

                    var postParams = new Dictionary<string, string>
                    {
                        { "login", AppInit.conf.Animelayer.login.u },
                        { "password", AppInit.conf.Animelayer.login.p }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        string configHost = AppInit.conf.Animelayer.host;
                        string baseHost = EnsureHttps(configHost);
                        string loginUrl = $"{baseHost}/auth/login/";
                        ParserLog.Write(TrackerName, "Attempting login", new Dictionary<string, object>
                        {
                            { "url", loginUrl },
                            { "configHost", configHost },
                            { "resolvedHost", baseHost },
                            { "user", AppInit.conf.Animelayer.login.u }
                        });

                        using (var response = await client.PostAsync(loginUrl, postContent))
                        {
                            var statusCode = (int)response.StatusCode;
                            ParserLog.Write(TrackerName, "Login response received", new Dictionary<string, object>
                            {
                                { "statusCode", statusCode },
                                { "status", response.StatusCode.ToString() }
                            });

                            var allCookies = new List<string>();
                            if (response.Headers.TryGetValues("Set-Cookie", out var headerCookies))
                            {
                                allCookies.AddRange(
                                    headerCookies
                                        .SelectMany(cookieHeader => cookieHeader.Split(new[] { ", " }, StringSplitOptions.None))
                                        .Select(part => part.Trim())
                                        .Where(trimmed => !string.IsNullOrWhiteSpace(trimmed)));
                            }

                            if (response.Content?.Headers != null && response.Content.Headers.TryGetValues("Set-Cookie", out var contentCookies))
                            {
                                allCookies.AddRange(
                                    contentCookies
                                        .SelectMany(cookieHeader => cookieHeader.Split(new[] { ", " }, StringSplitOptions.None))
                                        .Select(part => part.Trim())
                                        .Where(trimmed => !string.IsNullOrWhiteSpace(trimmed)));
                            }

                            if ((statusCode >= 300 && statusCode < 400) && allCookies.Count == 0)
                            {
                                ParserLog.Write(TrackerName, "Redirect response but no cookies found", new Dictionary<string, object>
                                {
                                    { "statusCode", statusCode },
                                    { "location", response.Headers.Location?.ToString() ?? "none" }
                                });
                            }

                            if (allCookies.Count > 0)
                            {
                                ParserLog.Write(TrackerName, "Cookies found in response", new Dictionary<string, object>
                                {
                                    { "cookieCount", allCookies.Count },
                                    { "cookies", string.Join(" | ", allCookies.Take(3)) }
                                });

                                string layerHash = null, layerId = null, phpsessid = null;
                                foreach (string cookieLine in allCookies.Where(c => !string.IsNullOrWhiteSpace(c)))
                                {
                                    if (cookieLine.Contains("layer_hash="))
                                    {
                                        var match = new Regex("layer_hash=([^;]+)(;|$)").Match(cookieLine);
                                        if (match.Success)
                                            layerHash = match.Groups[1].Value;
                                    }

                                    if (cookieLine.Contains("layer_id="))
                                    {
                                        var match = new Regex("layer_id=([^;]+)(;|$)").Match(cookieLine);
                                        if (match.Success)
                                            layerId = match.Groups[1].Value;
                                    }

                                    if (cookieLine.Contains("PHPSESSID="))
                                    {
                                        var match = new Regex("PHPSESSID=([^;]+)(;|$)").Match(cookieLine);
                                        if (match.Success)
                                            phpsessid = match.Groups[1].Value;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(layerHash) && !string.IsNullOrWhiteSpace(layerId))
                                {
                                    string cookieValue = $"layer_hash={layerHash};layer_id={layerId}";
                                    if (!string.IsNullOrWhiteSpace(phpsessid))
                                        cookieValue += $";PHPSESSID={phpsessid}";

                                    _memoryCache.Set(CookieCacheKey, cookieValue, DateTime.Now.AddDays(1));

                                    ParserLog.Write(TrackerName, "TakeLogin successful", new Dictionary<string, object>
                                    {
                                        { "user", AppInit.conf.Animelayer.login.u },
                                        { "hasLayerHash", !string.IsNullOrWhiteSpace(layerHash) },
                                        { "hasLayerId", !string.IsNullOrWhiteSpace(layerId) },
                                        { "hasPhpSessId", !string.IsNullOrWhiteSpace(phpsessid) }
                                    });
                                    return true;
                                }
                                else
                                {
                                    ParserLog.Write(TrackerName, "TakeLogin failed - missing required cookies", new Dictionary<string, object>
                                    {
                                        { "hasLayerHash", !string.IsNullOrWhiteSpace(layerHash) },
                                        { "hasLayerId", !string.IsNullOrWhiteSpace(layerId) },
                                        { "cookieLines", string.Join(" | ", allCookies) }
                                    });
                                }
                            }
                            else
                            {
                                string responseBody = null;
                                try
                                {
                                    responseBody = await response.Content.ReadAsStringAsync();
                                    if (responseBody.Length > 500)
                                        responseBody = responseBody.Substring(0, 500) + "...";
                                }
                                catch (OperationCanceledException ex)
                                {
                                    ParserLog.Write(TrackerName, "Failed to read response body", new Dictionary<string, object>
                                    {
                                        { "statusCode", statusCode },
                                        { "message", ex.Message },
                                        { "type", ex.GetType().Name }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    ParserLog.Write(TrackerName, "Failed to read response body", new Dictionary<string, object>
                                    {
                                        { "statusCode", statusCode },
                                        { "message", ex.Message },
                                        { "type", ex.GetType().Name }
                                    });
                                    throw;
                                }

                                ParserLog.Write(TrackerName, "TakeLogin failed - no cookies in response", new Dictionary<string, object>
                                {
                                    { "statusCode", statusCode },
                                    { "hasResponseBody", !string.IsNullOrWhiteSpace(responseBody) },
                                    { "responsePreview", responseBody }
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                       && ex is not StackOverflowException
                                       && ex is not ThreadAbortException)
            {
                ParserLog.Write(TrackerName, "TakeLogin error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }
            finally
            {
                loginSemaphore.Release();
            }

            return false;
        }

        static bool TryStartParse()
        {
            lock (workParseLock)
            {
                if (workParse)
                    return false;

                workParse = true;
                return true;
            }
        }

        static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0)
        {
            #region Authorization
            string cookie = null;
            bool needLogin = false;

            if (string.IsNullOrWhiteSpace(cookie))
            {
                if (string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.cookie))
                {
                    needLogin = true;
                }
                else
                {
                    ParserLog.Write(TrackerName, "Using static cookie from config", new Dictionary<string, object>());
                }
            }
            else
            {
                ParserLog.Write(TrackerName, "Validating cached cookie", new Dictionary<string, object>());
                if (!await ValidateCookie(cookie))
                {
                    ParserLog.Write(TrackerName, "Cached cookie is invalid, will re-login", new Dictionary<string, object>());
                    InvalidateCookie();
                    needLogin = true;
                }
                else
                {
                    ParserLog.Write(TrackerName, "Cached cookie is valid", new Dictionary<string, object>());
                }
            }

            if (needLogin)
            {
                if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) &&
                    !string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
                {
                    if (await TakeLoginAsync())
                    {
                        cookie = Cookie();
                        if (string.IsNullOrWhiteSpace(cookie))
                        {
                            ParserLog.Write(TrackerName, "Authorization failed", new Dictionary<string, object>
                            {
                                { "reason", "login succeeded but no cookie retrieved" }
                            });
                            return "work_login";
                        }

                        if (!await ValidateCookie(cookie))
                        {
                            ParserLog.Write(TrackerName, "Authorization failed", new Dictionary<string, object>
                            {
                                { "reason", "login cookie validation failed" }
                            });
                            InvalidateCookie();
                            return "work_login";
                        }
                    }
                    else
                    {
                        ParserLog.Write(TrackerName, "Authorization failed", new Dictionary<string, object>
                        {
                            { "reason", "login failed" }
                        });
                        return "work_login";
                    }
                }
                else
                {
                    ParserLog.Write(TrackerName, "Authorization failed", new Dictionary<string, object>
                    {
                        { "reason", "no cookie or credentials provided" }
                    });
                    return "Failed to authorize, please provide either cookie or credentials";
                }
            }
            #endregion

            if (!TryStartParse())
                return "work";

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = EnsureHttps(AppInit.conf.Animelayer.host);

                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write(TrackerName, $"Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                for (int page = startPage; page <= endPage; page++)
                {
                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Animelayer.parseDelay);

                    if (page > 1)
                    {
                        ParserLog.Write(TrackerName, $"Parsing page", new Dictionary<string, object>
                        {
                            { "page", page },
                            { "url", $"{baseUrl}/torrents/anime/?page={page}" }
                        });
                    }

                    var pageResult = await ParsePageWithRetry(page, baseUrl);

                    totalParsed += pageResult.parsed;
                    totalAdded += pageResult.added;
                    totalUpdated += pageResult.updated;
                    totalSkipped += pageResult.skipped;
                    totalFailed += pageResult.failed;
                }

                ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
                    new Dictionary<string, object>
                    {
                        { "parsed", totalParsed },
                        { "added", totalAdded },
                        { "updated", totalUpdated },
                        { "skipped", totalSkipped },
                        { "failed", totalFailed }
                    });
            }
            catch (OperationCanceledException oce)
            {
                ParserLog.Write(TrackerName, $"Canceled", new Dictionary<string, object>
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

                ParserLog.Write(TrackerName, $"Error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }
            finally
            {
                EndParse();
            }

            return "ok";
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> ParsePageWithRetry(int page, string baseUrl)
        {
            string cookie = Cookie();
            if (string.IsNullOrWhiteSpace(cookie))
            {
                ParserLog.Write(TrackerName, $"Page parse failed - no cookie", new Dictionary<string, object>
                {
                    { "page", page }
                });
                return (0, 0, 0, 0, 0);
            }

            var result = await ParsePage(page, cookie);

            if (result.parsed > 0)
                return result;

            ParserLog.Write(TrackerName, $"Page parse returned zeros, attempting cookie refresh", new Dictionary<string, object>
            {
                { "page", page },
                { "retryAttempt", 1 }
            });

            InvalidateCookie();

            string newCookie = null;

            if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.cookie))
            {
                newCookie = AppInit.conf.Animelayer.cookie;
                ParserLog.Write(TrackerName, "Using static cookie from config", new Dictionary<string, object>());
            }
            else if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) &&
                     !string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
            {
                if (await TakeLoginAsync())
                {
                    newCookie = Cookie();
                    ParserLog.Write(TrackerName, "Re-login successful", new Dictionary<string, object>());
                }
                else
                {
                    ParserLog.Write(TrackerName, "Re-login failed, aborting page parse", new Dictionary<string, object>
                    {
                        { "page", page }
                    });
                    return (0, 0, 0, 0, 0);
                }
            }
            else
            {
                ParserLog.Write(TrackerName, "No way to refresh cookie, aborting", new Dictionary<string, object>());
                return (0, 0, 0, 0, 0);
            }

            if (!string.IsNullOrWhiteSpace(newCookie))
                return await ParsePage(page, newCookie);

            return (0, 0, 0, 0, 0);
        }

        async Task<(int parsed, int added, int updated, int skipped, int failed)> ParsePage(int page, string cookie)
        {
            string baseHost = EnsureHttps(AppInit.conf.Animelayer.host);
            string url = $"{baseHost}/torrents/anime/" + (page > 1 ? $"?page={page}" : "");
            string html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);

            if (html == null)
            {
                ParserLog.Write(TrackerName, $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", "null response" }
                });
                return (0, 0, 0, 0, 0);
            }

            if (!html.Contains("id=\"wrapper\""))
            {
                bool isLoginForm = html.Contains("id=\"loginForm\"") || html.Contains("/auth/login/");

                ParserLog.Write(TrackerName, $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", "invalid content" },
                    { "likelyExpiredCookie", isLoginForm }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = AnimeLayerParser.ParseTorrentListFromHtml(html, baseHost, page);

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                    if (exists && _tcache.title == t.title)
                    {
                        skippedCount++;
                        ParserLog.WriteSkipped(TrackerName, _tcache, "no changes");
                        return true;
                    }

                    byte[] torrent = await HttpClient.Download($"{t.url}download/", cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy);
                    string magnet = BencodeTo.Magnet(torrent);
                    string sizeName = BencodeTo.SizeName(torrent);

                    if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                    {
                        t.magnet = magnet;
                        t.sizeName = sizeName;

                        if (exists)
                        {
                            updatedCount++;
                            ParserLog.WriteUpdated(TrackerName, t, "magnet from download");
                        }
                        else
                        {
                            addedCount++;
                            ParserLog.WriteAdded(TrackerName, t);
                        }
                        return true;
                    }

                    failedCount++;
                    ParserLog.WriteFailed(TrackerName, t, "could not get magnet or size");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write(TrackerName, $"Page {page} completed",
                    new Dictionary<string, object>
                    {
                        { "parsed", parsedCount },
                        { "added", addedCount },
                        { "updated", updatedCount },
                        { "skipped", skippedCount },
                        { "failed", failedCount }
                    });
            }

            return (parsedCount, addedCount, updatedCount, skippedCount, failedCount);
        }
    }
}

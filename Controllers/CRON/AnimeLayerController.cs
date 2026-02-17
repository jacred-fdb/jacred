using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/animelayer/[action]")]
    public class AnimeLayerController : BaseController
    {
        #region Cookie synchronization
        // Use SemaphoreSlim for async-safe synchronization (lock doesn't work well with async/await)
        // IMemoryCache is thread-safe, so we only need semaphore for login operations
        private static readonly SemaphoreSlim loginSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Ensures the host URL uses HTTPS. Automatically converts HTTP to HTTPS.
        /// Assumes hosts support both HTTP and HTTPS protocols.
        /// </summary>
        private static string EnsureHttps(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return host;

            // Convert HTTP to HTTPS
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "https://" + host.Substring(7);

            // If no protocol specified, add HTTPS
            if (!host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "https://" + host;

            return host;
        }
        #endregion

        #region TakeLogin
        /// <summary>
        /// Retrieves cached cookie for Animelayer authentication.
        /// Thread-safe: memoryCache is thread-safe, no additional locking needed for reads.
        /// </summary>
        /// <returns>Cached cookie string if available, null otherwise.</returns>
        private string Cookie()
        {
            // IMemoryCache is thread-safe for reads, no lock needed
            if (memoryCache.TryGetValue("animelayer:cookie", out string cookie))
            {
                // Strings are immutable in C#, so returning the reference is safe
                return cookie;
            }

            return null;
        }

        /// <summary>
        /// Invalidates the cached cookie (e.g., when it expires during parsing).
        /// Thread-safe: memoryCache is thread-safe.
        /// </summary>
        private void InvalidateCookie()
        {
            // IMemoryCache is thread-safe, no lock needed
            memoryCache.Remove("animelayer:cookie");
            ParserLog.Write("animelayer", "Cookie invalidated", new Dictionary<string, object>
            {
                { "reason", "likely expired during parsing" }
            });
        }

        /// <summary>
        /// Validates if the cached cookie is still valid by making a test request.
        /// Thread-safe: cookie parameter is immutable string, no synchronization needed.
        /// </summary>
        /// <param name="cookie">The cookie string to validate.</param>
        /// <returns>True if cookie is valid, false otherwise.</returns>
        async Task<bool> ValidateCookie(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie))
                return false;

            try
            {
                // Make a test request to check if cookie is valid
                string baseHost = EnsureHttps(AppInit.conf.Animelayer.host);
                string testUrl = $"{baseHost}/torrents/anime/";
                string html = await HttpClient.Get(testUrl, cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);

                if (html == null)
                {
                    ParserLog.Write("animelayer", "Cookie validation failed", new Dictionary<string, object>
                    {
                        { "reason", "null response" }
                    });
                    return false;
                }

                // Check if we got valid content (not login page)
                bool isValid = html.Contains("id=\"wrapper\"") && !html.Contains("id=\"loginForm\"") && !html.Contains("/auth/login/");

                ParserLog.Write("animelayer", "Cookie validation", new Dictionary<string, object>
                {
                    { "isValid", isValid },
                    { "hasWrapper", html.Contains("id=\"wrapper\"") },
                    { "hasLoginForm", html.Contains("id=\"loginForm\"") || html.Contains("/auth/login/") }
                });

                return isValid;
            }
            catch (OperationCanceledException)
            {
                // Let cancellation propagate to higher-level handlers (covers TaskCanceledException too)
                throw;
            }
            catch (HttpRequestException ex)
            {
                ParserLog.Write("animelayer", "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                return false;
            }
            catch (UriFormatException ex)
            {
                ParserLog.Write("animelayer", "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                return false;
            }
            catch (Exception ex)
            {
                ParserLog.Write("animelayer", "Cookie validation error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name }
                });
                throw;
            }
        }

        /// <summary>
        /// Attempts to login to Animelayer using configured credentials and cache the authentication cookie.
        /// Thread-safe: uses SemaphoreSlim to prevent concurrent login attempts (async-safe).
        /// </summary>
        /// <returns>True if login was successful and cookie was cached, false otherwise.</returns>
        [HttpGet]
        async public Task<bool> TakeLogin()
        {
            // Prevent concurrent login attempts using async-safe semaphore
            if (!await loginSemaphore.WaitAsync(0))
            {
                ParserLog.Write("animelayer", "TakeLogin skipped", new Dictionary<string, object>
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
                    ParserLog.Write("animelayer", "TakeLogin failed", new Dictionary<string, object>
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
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
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
                        // Use HTTPS and trailing slash as Python script shows
                        string configHost = AppInit.conf.Animelayer.host;
                        string baseHost = EnsureHttps(configHost);
                        string loginUrl = $"{baseHost}/auth/login/";
                        ParserLog.Write("animelayer", "Attempting login", new Dictionary<string, object>
                        {
                            { "url", loginUrl },
                            { "configHost", configHost },
                            { "resolvedHost", baseHost },
                            { "user", AppInit.conf.Animelayer.login.u }
                        });

                        using (var response = await client.PostAsync(loginUrl, postContent))
                        {
                            var statusCode = (int)response.StatusCode;
                            ParserLog.Write("animelayer", "Login response received", new Dictionary<string, object>
                            {
                                { "statusCode", statusCode },
                                { "status", response.StatusCode.ToString() }
                            });

                            // Collect cookies from initial response
                            // Some servers return multiple Set-Cookie headers, some return one header with comma-separated values
                            var allCookies = new List<string>();
                            if (response.Headers.TryGetValues("Set-Cookie", out var headerCookies))
                            {
                                allCookies.AddRange(
                                    headerCookies
                                        .SelectMany(cookieHeader => cookieHeader.Split(new[] { ", " }, StringSplitOptions.None))
                                        .Select(part => part.Trim())
                                        .Where(trimmed => !string.IsNullOrWhiteSpace(trimmed)));
                            }

                            // Also check content headers (some servers put cookies there)
                            if (response.Content?.Headers != null && response.Content.Headers.TryGetValues("Set-Cookie", out var contentCookies))
                            {
                                allCookies.AddRange(
                                    contentCookies
                                        .SelectMany(cookieHeader => cookieHeader.Split(new[] { ", " }, StringSplitOptions.None))
                                        .Select(part => part.Trim())
                                        .Where(trimmed => !string.IsNullOrWhiteSpace(trimmed)));
                            }

                            // Note: Cookies are typically in the initial response (302), not in redirect follow-up
                            // According to Python test, cookies are in the 302 response Set-Cookie header
                            if ((statusCode >= 300 && statusCode < 400) && allCookies.Count == 0)
                            {
                                ParserLog.Write("animelayer", "Redirect response but no cookies found", new Dictionary<string, object>
                                {
                                    { "statusCode", statusCode },
                                    { "location", response.Headers.Location?.ToString() ?? "none" }
                                });
                            }

                            if (allCookies.Count > 0)
                            {
                                ParserLog.Write("animelayer", "Cookies found in response", new Dictionary<string, object>
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

                                    // IMemoryCache.Set is thread-safe, no lock needed
                                    memoryCache.Set("animelayer:cookie", cookieValue, DateTime.Now.AddDays(1));

                                    ParserLog.Write("animelayer", "TakeLogin successful", new Dictionary<string, object>
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
                                    ParserLog.Write("animelayer", "TakeLogin failed - missing required cookies", new Dictionary<string, object>
                                    {
                                        { "hasLayerHash", !string.IsNullOrWhiteSpace(layerHash) },
                                        { "hasLayerId", !string.IsNullOrWhiteSpace(layerId) },
                                        { "cookieLines", string.Join(" | ", allCookies) }
                                    });
                                }
                            }
                            else
                            {
                                // Read response body to check for error messages
                                string responseBody = null;
                                try
                                {
                                    responseBody = await response.Content.ReadAsStringAsync();
                                    if (responseBody.Length > 500)
                                        responseBody = responseBody.Substring(0, 500) + "...";
                                }
                                catch (OperationCanceledException ex)
                                {
                                    ParserLog.Write("animelayer", "Failed to read response body", new Dictionary<string, object>
                                    {
                                        { "statusCode", statusCode },
                                        { "message", ex.Message },
                                        { "type", ex.GetType().Name }
                                    });
                                }
                                catch (TaskCanceledException ex)
                                {
                                    ParserLog.Write("animelayer", "Failed to read response body", new Dictionary<string, object>
                                    {
                                        { "statusCode", statusCode },
                                        { "message", ex.Message },
                                        { "type", ex.GetType().Name }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    // Log and rethrow unexpected exceptions to avoid silently swallowing bugs
                                    ParserLog.Write("animelayer", "Failed to read response body", new Dictionary<string, object>
                                    {
                                        { "statusCode", statusCode },
                                        { "message", ex.Message },
                                        { "type", ex.GetType().Name }
                                    });
                                    throw;
                                }

                                ParserLog.Write("animelayer", "TakeLogin failed - no cookies in response", new Dictionary<string, object>
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
                                       && ex is not System.Threading.ThreadAbortException)
            {
                ParserLog.Write("animelayer", "TakeLogin error", new Dictionary<string, object>
                {
                    { "message", ex.Message },
                    { "type", ex.GetType().Name },
                    { "stackTrace", ex.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
            }
            finally
            {
                // Release semaphore to allow next login attempt
                loginSemaphore.Release();
            }

            return false;
        }
        #endregion

        #region Parse
        static volatile bool workParse = false;
        private static readonly object workParseLock = new object();

        /// <summary>
        /// Attempts to start a parse operation. Returns true if successful, false if parsing is already in progress.
        /// </summary>
        /// <returns>True if parse was started, false if already running.</returns>
        private static bool TryStartParse()
        {
            lock (workParseLock)
            {
                if (workParse)
                    return false;

                workParse = true;
                return true;
            }
        }

        /// <summary>
        /// Ends the parse operation, allowing a new parse to start.
        /// </summary>
        private static void EndParse()
        {
            lock (workParseLock)
            {
                workParse = false;
            }
        }

        /// <summary>
        /// Parses torrent releases from Animelayer website pages.
        /// </summary>
        /// <param name="parseFrom">The starting page number to parse from. If 0 or less, defaults to page 1.</param>
        /// <param name="parseTo">The ending page number to parse to. If 0 or less, defaults to the same value as parseFrom.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a status string:
        /// - "work" if parsing is already in progress
        /// - "work_login" if cookie needs to be refreshed
        /// - "canceled" if the operation was canceled
        /// - "ok" if parsing completed successfully
        /// </returns>
        [HttpGet]
        async public Task<string> Parse(int parseFrom = 0, int parseTo = 0)
        {
            #region Authorization
            // Check if we need to get a cookie from login
            string cookie = null;
            bool needLogin = false;

            if (string.IsNullOrWhiteSpace(cookie))
            {
                // Try to use configured cookie first
                if (string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.cookie))
                {
                    needLogin = true;
                }
                else
                {
                    ParserLog.Write("animelayer", "Using static cookie from config", new Dictionary<string, object>());
                }
            }
            else
            {
                // Validate existing cookie
                ParserLog.Write("animelayer", "Validating cached cookie", new Dictionary<string, object>());
                if (!await ValidateCookie(cookie))
                {
                    ParserLog.Write("animelayer", "Cached cookie is invalid, will re-login", new Dictionary<string, object>());
                    InvalidateCookie();
                    needLogin = true;
                }
                else
                {
                    ParserLog.Write("animelayer", "Cached cookie is valid", new Dictionary<string, object>());
                }
            }

            // If we need to login, attempt it
            if (needLogin)
            {
                if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) &&
                    !string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
                {
                    if (await TakeLogin())
                    {
                        cookie = Cookie();
                        if (string.IsNullOrWhiteSpace(cookie))
                        {
                            ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
                            {
                                { "reason", "login succeeded but no cookie retrieved" }
                            });
                            return "work_login";
                        }

                        // Validate the newly obtained cookie
                        if (!await ValidateCookie(cookie))
                        {
                            ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
                            {
                                { "reason", "login cookie validation failed" }
                            });
                            InvalidateCookie();
                            return "work_login";
                        }
                    }
                    else
                    {
                        ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
                        {
                            { "reason", "login failed" }
                        });
                        return "work_login";
                    }
                }
                else
                {
                    ParserLog.Write("animelayer", "Authorization failed", new Dictionary<string, object>
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

                // Determine page range
                int startPage = parseFrom > 0 ? parseFrom : 1;
                int endPage = parseTo > 0 ? parseTo : (parseFrom > 0 ? parseFrom : 1);

                // Ensure startPage <= endPage
                if (startPage > endPage)
                {
                    int temp = startPage;
                    startPage = endPage;
                    endPage = temp;
                }

                ParserLog.Write("animelayer", $"Starting parse", new Dictionary<string, object>
                {
                    { "parseFrom", parseFrom },
                    { "parseTo", parseTo },
                    { "startPage", startPage },
                    { "endPage", endPage },
                    { "baseUrl", baseUrl }
                });

                int totalParsed = 0, totalAdded = 0, totalUpdated = 0, totalSkipped = 0, totalFailed = 0;

                // Parse pages from startPage to endPage
                for (int page = startPage; page <= endPage; page++)
                {
                    if (page > startPage)
                        await Task.Delay(AppInit.conf.Animelayer.parseDelay);

                    if (page > 1)
                    {
                        ParserLog.Write("animelayer", $"Parsing page", new Dictionary<string, object>
                        {
                            { "page", page },
                            { "url", $"{baseUrl}/torrents/anime/?page={page}" }
                        });
                    }

                    // Parse page (with automatic retry on cookie expiration)
                    var pageResult = await ParsePageWithRetry(page, baseUrl);

                    totalParsed += pageResult.parsed;
                    totalAdded += pageResult.added;
                    totalUpdated += pageResult.updated;
                    totalSkipped += pageResult.skipped;
                    totalFailed += pageResult.failed;
                }

                ParserLog.Write("animelayer", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)",
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
                ParserLog.Write("animelayer", $"Canceled", new Dictionary<string, object>
                {
                    { "message", oce.Message },
                    { "stackTrace", oce.StackTrace?.Split('\n').FirstOrDefault() ?? "" }
                });
                return "canceled";
            }
            catch (Exception ex)
            {
                // Rethrow critical exceptions that should never be swallowed
                if (ex is OutOfMemoryException)
                    throw;

                ParserLog.Write("animelayer", $"Error", new Dictionary<string, object>
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
        #endregion


        #region ParsePageWithRetry
        /// <summary>
        /// Parses a single page with automatic retry if cookie expires during parsing.
        /// Thread-safe: ensures cookie is retrieved and copied before async operations.
        /// </summary>
        /// <param name="page">The page number to parse.</param>
        /// <param name="baseUrl">The base URL for the tracker.</param>
        /// <returns>A task that represents the asynchronous operation with parsing statistics.</returns>
        async Task<(int parsed, int added, int updated, int skipped, int failed)> ParsePageWithRetry(int page, string baseUrl)
        {
            // Get cookie copy before async operations to prevent race conditions
            string cookie = Cookie();
            if (string.IsNullOrWhiteSpace(cookie))
            {
                ParserLog.Write("animelayer", $"Page parse failed - no cookie", new Dictionary<string, object>
                {
                    { "page", page }
                });
                return (0, 0, 0, 0, 0);
            }

            var result = await parsePage(page, cookie);

            // If we got results, success
            if (result.parsed > 0)
                return result;

            // If parsing failed (no results), might be due to expired cookie - try once more
            ParserLog.Write("animelayer", $"Page parse returned zeros, attempting cookie refresh", new Dictionary<string, object>
            {
                { "page", page },
                { "retryAttempt", 1 }
            });

            // Invalidate cached cookie and try to get a fresh one
            InvalidateCookie();

            string newCookie = null;

            // Try to refresh cookie
            if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.cookie))
            {
                // Use static cookie from config
                newCookie = AppInit.conf.Animelayer.cookie;
                ParserLog.Write("animelayer", "Using static cookie from config", new Dictionary<string, object>());
            }
            else if (!string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.u) &&
                     !string.IsNullOrWhiteSpace(AppInit.conf.Animelayer.login?.p))
            {
                // Attempt re-login
                if (await TakeLogin())
                {
                    newCookie = Cookie();
                    ParserLog.Write("animelayer", "Re-login successful", new Dictionary<string, object>());
                }
                else
                {
                    ParserLog.Write("animelayer", "Re-login failed, aborting page parse", new Dictionary<string, object>
                    {
                        { "page", page }
                    });
                    return (0, 0, 0, 0, 0);
                }
            }
            else
            {
                ParserLog.Write("animelayer", "No way to refresh cookie, aborting", new Dictionary<string, object>());
                return (0, 0, 0, 0, 0);
            }

            // Retry the page with new cookie (already a local copy, safe for async)
            if (!string.IsNullOrWhiteSpace(newCookie))
            {
                return await parsePage(page, newCookie);
            }

            return (0, 0, 0, 0, 0);
        }
        #endregion


        #region parsePage
        /// <summary>
        /// Parses a single page of torrent releases from the Animelayer website.
        /// </summary>
        /// <param name="page">The page number to parse.</param>
        /// <param name="cookie">The authentication cookie to use for the request.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task result contains a tuple with parsing statistics:
        /// - parsed: Total number of torrent releases found and processed
        /// - added: Number of new torrent releases added to the database
        /// - updated: Number of existing torrent releases that were updated
        /// - skipped: Number of torrent releases skipped (no changes detected)
        /// - failed: Number of torrent releases that failed to process
        /// </returns>
        async Task<(int parsed, int added, int updated, int skipped, int failed)> parsePage(int page, string cookie)
        {
            string baseHost = EnsureHttps(AppInit.conf.Animelayer.host);
            string url = $"{baseHost}/torrents/anime/" + (page > 1 ? $"?page={page}" : "");
            string html = await HttpClient.Get(url, cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);

            if (html == null)
            {
                ParserLog.Write("animelayer", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", "null response" }
                });
                return (0, 0, 0, 0, 0);
            }

            // Check if we have valid content or got redirected/logged out
            if (!html.Contains("id=\"wrapper\""))
            {
                // Additional check: if response contains login form, cookie likely expired
                bool isLoginForm = html.Contains("id=\"loginForm\"") || html.Contains("/auth/login/");

                ParserLog.Write("animelayer", $"Page parse failed", new Dictionary<string, object>
                {
                    { "page", page },
                    { "url", url },
                    { "reason", "invalid content" },
                    { "likelyExpiredCookie", isLoginForm }
                });
                return (0, 0, 0, 0, 0);
            }

            var torrents = new List<TorrentDetails>();
            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("class=\"torrent-item torrent-item-medium panel\"").Skip(1))
            {

                #region Local method - Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Creation date
                DateTime createTime = default;

                // Match Russian text: "Добавл" (Added) or "Обновл" (Updated)
                if (Regex.IsMatch(row, "(Добавл|Обновл)[^<]+</span>[0-9]+ [^ ]+ [0-9]{4}"))
                {
                    createTime = tParse.ParseCreateTime(Match(">(Добавл|Обновл)[^<]+</span>([0-9]+ [^ ]+ [0-9]{4})", 2), "dd.MM.yyyy");
                }
                else
                {
                    string date = Match("(Добавл|Обновл)[^<]+</span>([^\n]+) в", 2);
                    if (string.IsNullOrWhiteSpace(date))
                        continue;

                    createTime = tParse.ParseCreateTime($"{date} {DateTime.Today.Year}", "dd.MM.yyyy");
                }

                if (createTime == default)
                {
                    if (page != 1)
                        continue;

                    createTime = DateTime.UtcNow;
                }
                #endregion

                #region Release data
                var gurl = Regex.Match(row, "<a href=\"/(torrent/[a-z0-9]+)/?\">([^<]+)</a>").Groups;

                string urlPath = gurl[1].Value;
                string title = gurl[2].Value;

                string _sid = Match("class=\"icon s-icons-upload\"></i>([0-9]+)");
                string _pir = Match("class=\"icon s-icons-download\"></i>([0-9]+)");

                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                    continue;

                // Match Russian text: "Разрешение" (Resolution)
                if (Regex.IsMatch(row, "Разрешение: ?</strong>1920x1080"))
                    title += " [1080p]";
                else if (Regex.IsMatch(row, "Разрешение: ?</strong>1280x720"))
                    title += " [720p]";

                string fullUrl = $"{baseHost}/{urlPath}/";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Example format: "Original Name (2021) / Russian Name [TV] (1-7)"
                var g = Regex.Match(title, "([^/\\[\\(]+)\\([0-9]{4}\\)[^/]+/([^/\\[\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[2].Value.Trim();
                    originalname = g[1].Value.Trim();
                }
                else
                {
                    // Example format: "Original Name / Russian Name (1—6)"
                    g = Regex.Match(title, "^([^/\\[\\(]+)/([^/\\[\\(]+)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[2].Value.Trim();
                        originalname = g[1].Value.Trim();
                    }
                }
                #endregion

                // Release year (matches Russian text: "Год выхода")
                if (!int.TryParse(Match("Год выхода: ?</strong>([0-9]{4})"), out int relased) || relased == 0)
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "animelayer",
                        types = ["anime"],
                        url = fullUrl,
                        title = title,
                        sid = sid,
                        pir = pir,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            int parsedCount = torrents.Count;
            int addedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            // If we found torrents, process them
            if (torrents.Count > 0)
            {
                await FileDB.AddOrUpdate(torrents, async (t, db) =>
                {
                    // Check if already exists
                    bool exists = db.TryGetValue(t.url, out TorrentDetails _tcache);

                    // Check if already exists with same title (skip if unchanged)
                    if (exists && _tcache.title == t.title)
                    {
                        skippedCount++;
                        // Use existing cache data for logging
                        ParserLog.WriteSkipped("animelayer", _tcache, "no changes");
                        return true;
                    }

                    // Try to download torrent file
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
                            ParserLog.WriteUpdated("animelayer", t, "magnet from download");
                        }
                        else
                        {
                            addedCount++;
                            ParserLog.WriteAdded("animelayer", t);
                        }
                        return true;
                    }

                    failedCount++;
                    ParserLog.WriteFailed("animelayer", t, "could not get magnet or size");
                    return false;
                });
            }

            if (parsedCount > 0)
            {
                ParserLog.Write("animelayer", $"Page {page} completed",
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
        #endregion
    }
}

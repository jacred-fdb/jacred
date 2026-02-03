using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using System.Collections.Generic;
using JacRed.Engine;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/baibako/[action]")]
    public class BaibakoController : BaseController
    {
        #region TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("baibako:cookie", out string cookie))
                return cookie;

            return null;
        }

        async public Task<bool> TakeLogin()
        {
            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "username", AppInit.conf.Baibako.login.u },
                        { "password", AppInit.conf.Baibako.login.p }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Baibako.host}/takelogin.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string sessid = null, pass = null, uid = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("PHPSESSID="))
                                        sessid = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("pass="))
                                        pass = new Regex("pass=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("uid="))
                                        uid = new Regex("uid=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
                                {
                                    memoryCache.Set("baibako:cookie", $"PHPSESSID={sessid}; uid={uid}; pass={pass}", DateTime.Now.AddDays(1));
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
        #endregion


        #region Parse
        static bool workParse = false;

        async public Task<string> Parse(int maxpage)
        {
            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                if (await TakeLogin() == false)
                    return "Не удалось авторизоваться";
            }
            #endregion

            if (workParse)
                return "work";

            workParse = true;

            try
            {
                var sw = Stopwatch.StartNew();
                string baseUrl = $"{AppInit.conf.Baibako.host}/browse.php";
                ParserLog.Write("baibako", $"Starting parse maxpage={maxpage}, base: {baseUrl}");
                for (int page = 0; page <= maxpage; page++)
                {
                    if (page > 1)
                        await Task.Delay(AppInit.conf.Baibako.parseDelay);

                    ParserLog.Write("baibako", $"Page {page}: {baseUrl}?page={page}");
                    await parsePage(page);
                }
                ParserLog.Write("baibako", $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
            }
            catch (Exception ex)
            {
                ParserLog.Write("baibako", $"Error: {ex.Message}");
            }
            finally
            {
                workParse = false;
            }

            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.Baibako.host}/browse.php?page={page}", encoding: Encoding.GetEncoding(1251), cookie: Cookie(memoryCache));
            if (html == null || !html.Contains("id=\"navtop\""))
                return false;

            var torrents = new List<BaibakoDetails>();

            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("<tr").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                // Дата создания
                DateTime createTime = tParse.ParseCreateTime(Match("<small>Загружена: ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"), "dd.MM.yyyy");
                if (createTime == default)
                {
                    if (page != 0)
                        continue;

                    createTime = DateTime.UtcNow;
                }

                #region Данные раздачи
                var gurl = Regex.Match(row, "<a href=\"/?(details.php\\?id=[0-9]+)[^\"]+\">([^<]+)</a>").Groups;

                string url = gurl[1].Value;
                string title = gurl[2].Value;
                title = title.Replace("(Обновляемая)", "").Replace("(Золото)", "").Replace("(Оновлюється)", "");
                title = Regex.Replace(title, "/( +| )?$", "").Trim();

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || !Regex.IsMatch(title, "(1080p|720p)"))
                    continue;

                url = $"{AppInit.conf.Baibako.host}/{url}";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // 9-1-1 /9-1-1 /s04e01-13 /WEBRip XviD
                var g = Regex.Match(title, "([^/\\(]+)[^/]+/([^/\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string download = Match("href=\"/?(download.php\\?id=([0-9]+))\"");
                    if (string.IsNullOrWhiteSpace(download))
                        continue;

                    torrents.Add(new BaibakoDetails()
                    {
                        trackerName = "baibako",
                        types = new string[] { "serial" },
                        url = url,
                        title = title,
                        sid = 1,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        downloadUri = $"{AppInit.conf.Baibako.host}/{download}"
                    });
                }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] torrent = await HttpClient.Download(t.downloadUri, cookie: Cookie(memoryCache), referer: $"{AppInit.conf.Baibako.host}/browse.php");
                string magnet = BencodeTo.Magnet(torrent);
                string sizeName = BencodeTo.SizeName(torrent);

                if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
                {
                    t.magnet = magnet;
                    t.sizeName = sizeName;
                    return true;
                }

                return false;
            });

            return torrents.Count > 0;
        }
        #endregion
    }
}

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine;
using JacRed.Models.Details;
using System.Collections.Generic;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/lostfilm/[action]")]
    public class LostfilmController : BaseController
    {
        #region LostfilmController
        static System.Net.Http.HttpClient cloudHttp;

        static LostfilmController()
        {
            //var handler = new ClearanceHandler("http://ip:8191/")
            //{
            //    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36",
            //    MaxTimeout = 60000
            //};

            cloudHttp = new System.Net.Http.HttpClient(); // handler
            cloudHttp.Timeout = TimeSpan.FromSeconds(20);
            cloudHttp.MaxResponseContentBufferSize = 10_000_000;
            cloudHttp.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36");
            cloudHttp.DefaultRequestHeaders.Add("cookie", AppInit.conf.Lostfilm.cookie);

            cloudHttp.DefaultRequestHeaders.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            cloudHttp.DefaultRequestHeaders.Add("accept-language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5");
            cloudHttp.DefaultRequestHeaders.Add("cache-control", "no-cache");
            cloudHttp.DefaultRequestHeaders.Add("dnt", "1");
            cloudHttp.DefaultRequestHeaders.Add("pragma", "no-cache");
            cloudHttp.DefaultRequestHeaders.Add("sec-ch-ua", "\"Chromium\";v=\"92\", \" Not A;Brand\";v=\"99\", \"Google Chrome\";v=\"92\"");
            cloudHttp.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            cloudHttp.DefaultRequestHeaders.Add("sec-fetch-dest", "document");
            cloudHttp.DefaultRequestHeaders.Add("sec-fetch-mode", "navigate");
            cloudHttp.DefaultRequestHeaders.Add("sec-fetch-site", "none");
            cloudHttp.DefaultRequestHeaders.Add("sec-fetch-user", "?1");
            cloudHttp.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");
        }
        #endregion

        #region getMagnet
        async Task<(string magnet, string quality, string sizeName)> getMagnet(string url)
        {
            try
            {
                string fullNews = await cloudHttp.GetStringAsync(url);
                if (!string.IsNullOrWhiteSpace(fullNews))
                {
                    // Id серии
                    string episodeId = new Regex("\"PlayEpisode\\('([0-9]+)'\\)\"").Match(fullNews).Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(episodeId))
                    {
                        //Console.WriteLine($"https://www.lostfilm.tv/v_search.php?a={episodeId}");

                        // Получаем ссылку на поиск
                        string v_search = await cloudHttp.GetStringAsync($"{AppInit.conf.Lostfilm.host}/v_search.php?a={episodeId}");
                        string retreSearchUrl = new Regex("url=(\")?(https?://[^/]+/[^\"]+)").Match(v_search ?? "").Groups[2].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(retreSearchUrl))
                        {
                            //Console.WriteLine(retreSearchUrl);

                            // Загружаем HTML поиска
                            string shtml = await cloudHttp.GetStringAsync(retreSearchUrl);
                            if (!string.IsNullOrWhiteSpace(shtml))
                            {
                                //Console.WriteLine("7");
                                var match = new Regex("<div class=\"inner-box--link main\"><a href=\"([^\"]+)\">([^<]+)</a></div>").Match(Regex.Replace(shtml, "[\n\r\t]+", ""));
                                while (match.Success)
                                {
                                    if (Regex.IsMatch(match.Groups[2].Value, "(2160p|2060p|1440p|1080p|720p)", RegexOptions.IgnoreCase))
                                    {
                                        string torrentFile = match.Groups[1].Value;
                                        string quality = Regex.Match(match.Groups[2].Value, "(2160p|2060p|1440p|1080p|720p)").Groups[1].Value;

                                        if (!string.IsNullOrWhiteSpace(torrentFile) && !string.IsNullOrWhiteSpace(quality))
                                        {
                                            //Console.WriteLine("d: " + torrentFile);
                                            byte[] torrent = await HttpClient.Download(torrentFile, referer: $"{AppInit.conf.Lostfilm.host}/");
                                            string magnet = BencodeTo.Magnet(torrent);
                                            if (!string.IsNullOrWhiteSpace(magnet))
                                            {
                                                string sizeName = BencodeTo.SizeName(torrent);
                                                if (!string.IsNullOrWhiteSpace(sizeName))
                                                    return (magnet, quality, sizeName);
                                            }
                                        }
                                    }

                                    match = match.NextMatch();
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return default;
        }
        #endregion


        #region Parse
        static bool _workParse = false;

        async public Task<string> Parse(int maxpage = 1)
        {
            if (_workParse)
                return "work";

            _workParse = true;

            try
            {
                for (int i = 1; i <= maxpage; i++)
                {
                    if (i > 1)
                        await Task.Delay(AppInit.conf.Lostfilm.parseDelay);

                    await parsePage(i);
                }
            }
            catch { }
            finally
            {
                _workParse = false;
            }

            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page)
        {
            string html = await HttpClient.Get(page > 1 ? $"{AppInit.conf.Lostfilm.host}/new/page_{page}" : $"{AppInit.conf.Lostfilm.host}/new/", useproxy: AppInit.conf.Lostfilm.useproxy, httpversion: 2);
            if (html == null || !html.Contains("LostFilm.TV</title>"))
                return false;

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"hor-breaker dashed\"").Skip(1))
            {
                try
                {
                    #region Локальный метод - Match
                    string Match(string pattern, int index = 1)
                    {
                        string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                        res = Regex.Replace(res, "[\n\r\t ]+", " ");
                        return res.Trim();
                    }
                    #endregion

                    if (string.IsNullOrWhiteSpace(row))
                        continue;

                    #region Дата создания
                    DateTime createTime = tParse.ParseCreateTime(Match("<div class=\"right-part\">([0-9]{2}\\.[0-9]{2}\\.[0-9]{4})</div>"), "dd.MM.yyyy");

                    if (createTime == default)
                    {
                        if (page != 1)
                            continue;

                        createTime = DateTime.UtcNow;
                    }
                    #endregion

                    #region Данные раздачи
                    string url = Match("href=\"/([^\"]+)\"");
                    string sinfo = Match("<div class=\"left-part\">([^<]+)</div>");
                    string name = Match("<div class=\"name-ru\">([^<]+)</div>");
                    string originalname = Match("<div class=\"name-en\">([^<]+)</div>");

                    if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("series/") || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(originalname) || string.IsNullOrWhiteSpace(sinfo))
                        continue;

                    url = $"{AppInit.conf.Lostfilm.host}/{url}";
                    #endregion

                    #region relased
                    int relased = 0;

                    string serieName = Regex.Match(url, "https?://www.lostfilm.tv/series/([^/]+)").Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(serieName))
                        continue;

                    string relasedPath = $"Data/temp/lostfilm/{serieName}.relased";
                    if (System.IO.File.Exists(relasedPath))
                    {
                        relased = int.Parse(System.IO.File.ReadAllText(relasedPath));
                    }
                    else
                    {
                        string series = await cloudHttp.GetStringAsync($"{AppInit.conf.Lostfilm.host}/series/{serieName}");
                        if (series != null)
                        {
                            string dateCreated = Regex.Match(series, "itemprop=\"dateCreated\" content=\"([0-9]{4})-[0-9]{2}-[0-9]{2}\"").Groups[1].Value;
                            if (int.TryParse(dateCreated, out int _date) && _date > 0)
                                relased = _date;
                        }

                        if (relased > 0)
                            System.IO.File.WriteAllText(relasedPath, relased.ToString());
                        else
                            continue;
                    }
                    #endregion

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "lostfilm",
                        types = new string[] { "serial" },
                        url = url,
                        title = $"{name} / {originalname} / {sinfo} [{relased}]",
                        sid = 1,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
                catch { }
            }

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && !string.IsNullOrWhiteSpace(_tcache.magnet))
                    return true;

                var mag = await getMagnet(t.url);
                if (!string.IsNullOrWhiteSpace(mag.magnet))
                {
                    if (!string.IsNullOrWhiteSpace(mag.quality))
                        t.title = t.title.Replace("]", $", {mag.quality}]");

                    t.magnet = mag.magnet;
                    t.sizeName = mag.sizeName;
                    return true;
                }

                return false;
            });

            return torrents.Count > 0;
        }
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Models.Details;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/animelayer/[action]")]
    public class AnimeLayerController : BaseController
    {

        #region Parse
        static bool workParse = false;

        /// <param name="pageTo">Конечная страница.</param>
        [HttpGet]
        async public Task<string> Parse(int pageTo = 1)
        {
            #region Авторизация
            if (AppInit.conf.Animelayer.cookie == null)
            {
                return "Не удалось авторизоваться, укажи правильные cookie: layer_hash, layer_id.";

            }
            #endregion

            if (workParse)
                return "work";

            workParse = true;

            try
            {
                for (int page = 1; page <= pageTo; page++)
                {
                    if (page > 1)
                        await Task.Delay(AppInit.conf.Animelayer.parseDelay);

                    await parsePage(page);
                }
            }
            catch { }
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

            string cookie = AppInit.conf.Animelayer.cookie;
            string html = await HttpClient.Get($"{AppInit.conf.Animelayer.host}/torrents/anime/?page={page}", cookie: cookie, useproxy: AppInit.conf.Animelayer.useproxy, httpversion: 2);
            if (html == null || !html.Contains("id=\"wrapper\""))
                return false;

            var torrents = new List<TorrentBaseDetails>();
            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("class=\"torrent-item torrent-item-medium panel\"").Skip(1))
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

                #region Дата создания
                DateTime createTime = default;

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

                #region Данные раздачи
                var gurl = Regex.Match(row, "<a href=\"/(torrent/[a-z0-9]+)/?\">([^<]+)</a>").Groups;

                string url = gurl[1].Value;
                string title = gurl[2].Value;

                string _sid = Match("class=\"icon s-icons-upload\"></i>([0-9]+)");
                string _pir = Match("class=\"icon s-icons-download\"></i>([0-9]+)");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                    continue;

                if (Regex.IsMatch(row, "Разрешение: ?</strong>1920x1080"))
                    title += " [1080p]";
                else if (Regex.IsMatch(row, "Разрешение: ?</strong>1280x720"))
                    title += " [720p]";

                url = $"{AppInit.conf.Animelayer.host}/{url}/";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Shaman king (2021) / Король-шаман [ТВ] (1-7)
                var g = Regex.Match(title, "([^/\\[\\(]+)\\([0-9]{4}\\)[^/]+/([^/\\[\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[2].Value.Trim();
                    originalname = g[1].Value.Trim();
                }
                else
                {
                    // Shadows House / Дом теней (1—6)
                    g = Regex.Match(title, "^([^/\\[\\(]+)/([^/\\[\\(]+)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[2].Value.Trim();
                        originalname = g[1].Value.Trim();
                    }
                }
                #endregion

                // Год выхода
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
                        types = new[] { "anime" },
                        url = url,
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

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;
                byte[] torrent = await HttpClient.Download($"{t.url}download/", cookie: cookie);
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

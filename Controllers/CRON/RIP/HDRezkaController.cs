using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/hdrezka/[action]")]
    public class HDRezkaController : BaseController
    {
        #region Parse
        static bool workParse = false;

        async public Task<string> Parse(int maxpage = 1)
        {
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("hdrezka", StringComparer.OrdinalIgnoreCase))
                return "disabled";
            if (workParse)
                return "work";

            workParse = true;

            try
            {
                for (int page = 1; page <= maxpage; page++)
                {
                    if (page > 1)
                        await Task.Delay(AppInit.conf.Rezka.parseDelay);

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
            string html = await HttpClient.Get(AppInit.conf.Rezka.rqHost() + (page > 1 ? $"/page/{page}" : ""), useproxy: AppInit.conf.Rezka.useproxy);
            if (html == null || !html.Contains("id=\"main_wrapper\""))
                return false;

            var torrents = new List<TorrentBaseDetails>();

            foreach (string row in tParse.ReplaceBadNames(html).Split("<a ").Skip(1).Reverse())
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row) || !row.Contains("class=\"card-item\""))
                    continue;

                string url = Match("href=\"/([^\"]+)\"");
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                #region types
                string[] types = null;
                string type = Match("class=\"card-item-type ([^\" ]+)\"");

                switch (type)
                {
                    case "films":
                        types = new string[] { "movie" };
                        break;
                    case "series":
                        types = new string[] { "serial" };
                        break;
                    case "cartoons":
                        types = new string[] { "multfilm", "multserial" };
                        break;
                    case "animation":
                        types = new string[] { "anime" };
                        break;
                }

                if (types == null)
                    continue;
                #endregion

                if (!string.IsNullOrWhiteSpace(url))
                {
                    url = $"{AppInit.conf.Rezka.host}/{url}";
                    if (memoryCache.TryGetValue(url, out _))
                        continue;

                    string fulnews = await HttpClient.Get(url, useproxy: AppInit.conf.Rezka.useproxy);
                    if (fulnews == null)
                        continue;

                    string name = Regex.Match(fulnews, "class=\"si-title\">([^<]+)<").Groups[1].Value.Split("/")[0].Trim();

                    string siparam = Regex.Match(fulnews, "class=\"si-param\">(s[0-9]+e[0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(siparam) && type == "series")
                    {
                        siparam = Regex.Match(fulnews, "class=\"si-param\">(s[0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(siparam))
                            continue;
                    }

                    var g = Regex.Match(fulnews, "<div class=\"si-data\">[\n\r\t ]+<ul>[\n\r\t ]+<li>([^<]+)</li>[\n\r\t ]+<li>([0-9]{4})").Groups;
                    string originalname = g[1].Value.Split("/")[0].Trim();
                    if (!int.TryParse(g[2].Value, out int relased) || relased == 0)
                        continue;

                    #region Дата создания
                    DateTime createTime = tParse.ParseCreateTime(Regex.Match(fulnews, "class=\"si-date\">(Добавлено|Опубликовано|Обновлено) ([^<]+)<").Groups[2].Value, "dd.MM.yyyy");

                    if (createTime == default)
                    {
                        if (page != 1)
                            continue;

                        createTime = DateTime.UtcNow;
                    }
                    #endregion

                    #region Обновляем/Получаем Magnet
                    string magnet = null;
                    string sizeName = null;
                    string quality = null;

                    byte[] torrent = null;

                    foreach (string q in new string[] { "1080p", "720p" })
                    {
                        string tid = Regex.Match(fulnews, $"href=\"/([^\"]+)\" class=\"dwn-links-item\">{q}</a>").Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(tid))
                            continue;

                        torrent = await HttpClient.Download($"{AppInit.conf.Rezka.rqHost()}/{tid}", referer: url, useproxy: AppInit.conf.Rezka.useproxy);
                        magnet = BencodeTo.Magnet(torrent);
                        sizeName = BencodeTo.SizeName(torrent);
                        quality = q;

                        if (!string.IsNullOrWhiteSpace(magnet))
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(magnet))
                        continue;
                    #endregion

                    string info = fulnews.Contains("<img title=\"Украинский\"") ? ", UKR" : fulnews.Contains("<u>ненормативная лексика</u>") ? ", 18+" : null;

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "hdrezka",
                        types = types,
                        url = url,
                        title = $"{name} / {originalname} {(string.IsNullOrWhiteSpace(siparam) ? "" : $"/ {siparam.ToLower()} ")}[{relased}, {quality}{info}]",
                        sid = 1,
                        sizeName = sizeName,
                        createTime = createTime,
                        magnet = magnet,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });

                    memoryCache.Set(url, 0, DateTime.Today.AddDays(1));
                }
            }

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
        #endregion
    }
}

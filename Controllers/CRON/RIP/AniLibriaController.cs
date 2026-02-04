using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using Microsoft.AspNetCore.Mvc;
using JacRed.Models.tParse.AniLibria;
using JacRed.Engine;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/anilibria/[action]")]
    public class AniLibriaController : BaseController
    {
        static bool workParse = false;

        async public Task<string> Parse(int limit)
        {
            if (AppInit.conf?.disable_trackers != null && AppInit.conf.disable_trackers.Contains("anilibria", StringComparer.OrdinalIgnoreCase))
                return "disabled";
            if (workParse)
                return "work";

            workParse = true;

            try
            {
                for (int after = 0; after <= limit; after++)
                {
                    after = after + 40;

                    var roots = await HttpClient.Get<List<RootObject>>($"{AppInit.conf.Anilibria.rqHost()}/v2/getUpdates?limit=40&after={after - 40}&include=raw_torrent", IgnoreDeserializeObject: true, useproxy: AppInit.conf.Anilibria.useproxy);
                    if (roots == null || roots.Count == 0)
                        continue;

                    foreach (var root in roots)
                    {
                        var torrents = new List<TorrentBaseDetails>();
                        DateTime createTime = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(root.last_change > root.updated ? root.last_change : root.updated);

                        foreach (var torrent in root.torrents.list)
                        {
                            if (string.IsNullOrWhiteSpace(root.code) || 480 >= torrent.quality.resolution && string.IsNullOrWhiteSpace(torrent.quality.encoder) && string.IsNullOrWhiteSpace(torrent.url))
                                continue;

                            // Данные раздачи
                            string url = $"anilibria.tv:{root.code}:{torrent.quality.resolution}:{torrent.quality.encoder}";
                            string title = $"{root.names.ru} / {root.names.en} {root.season.year} (s{root.season.code}, e{torrent.series.@string}) [{torrent.quality.@string}]";

                            #region Получаем/Обновляем магнет
                            if (string.IsNullOrWhiteSpace(torrent.raw_base64_file))
                                continue;

                            byte[] _t = Convert.FromBase64String(torrent.raw_base64_file);
                            string magnet = BencodeTo.Magnet(_t);
                            string sizeName = BencodeTo.SizeName(_t);

                            if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName))
                                continue;
                            #endregion

                            torrents.Add(new TorrentBaseDetails()
                            {
                                trackerName = "anilibria",
                                types = new string[] { "anime" },
                                url = url,
                                title = title,
                                sid = torrent.seeders,
                                pir = torrent.leechers,
                                createTime = createTime,
                                magnet = magnet,
                                sizeName = sizeName,
                                name = tParse.ReplaceBadNames(root.names.ru),
                                originalname = tParse.ReplaceBadNames(root.names.en),
                                relased = root.season.year
                            });
                        }

                        FileDB.AddOrUpdate(torrents);
                    }

                    roots = null;
                }
            }
            catch { }
            finally
            {
                workParse = false;
            }

            return "ok";
        }
    }
}

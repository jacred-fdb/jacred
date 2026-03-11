using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;
using JacRed.Models.Details;

namespace JacRed.Controllers.CRON
{
    /// <summary>
    /// Парсинг Bitru через официальный API (api.php?get=torrents).
    /// Лимит: макс. 5 запросов в сек на IP — между запросами задержка 250 ms.
    /// </summary>
    [Route("/cron/bitruapi/[action]")]
    public class BitruApiController : BaseController
    {
        const string ApiGetTorrents = "torrents";
        /// <summary>Задержка между запросами к API и скачиванием файлов (5 req/sec → 250 ms)</summary>
        const int ApiDelayMs = 250;

        static readonly string ApiUrl;
        static readonly string HostUrl;
        static readonly string LastNewTorPath = "Data/temp/bitruapi_lastnewtor.txt";

        static BitruApiController()
        {
            var host = AppInit.conf.Bitru?.host?.TrimEnd('/') ?? "https://bitru.org";
            ApiUrl = $"{host}/api.php";
            HostUrl = host;
        }

        #region Parse (последние торренты)

        static bool _workParse = false;

        /// <summary>Загрузить последние торренты (movie + serial), limit 100. Учитывает лимит 5 req/sec.</summary>
        async public Task<string> Parse(int limit = 100)
        {
            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write("bitruapi", $"Parse start, limit={limit}, api={ApiUrl}");

                var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: null);
                if (torrents != null && torrents.Count > 0)
                {
                    await SaveTorrentsAndMagnets(torrents);
                    log = $"saved {torrents.Count}";
                }
                else
                    log = "no items";

                ParserLog.Write("bitruapi", $"Parse completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
            }
            catch (Exception ex)
            {
                ParserLog.Write("bitruapi", $"Error: {ex.Message}");
                log = $"error: {ex.Message}";
            }
            finally
            {
                _workParse = false;
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }

        #endregion

        #region ParseFromDate (торренты после даты lastnewtor)

        /// <summary>
        /// Загрузить торренты, добавленные после указанной даты (lastnewtor).
        /// Пример: /cron/bitruapi/ParseFromDate?lastnewtor=23.10.2025
        /// Формат даты: dd.MM.yyyy
        /// </summary>
        async public Task<string> ParseFromDate(string lastnewtor, int limit = 100)
        {
            if (string.IsNullOrWhiteSpace(lastnewtor))
                return "bad lastnewtor (use dd.MM.yyyy)";

            if (!DateTime.TryParseExact(lastnewtor.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fromDate))
                return "bad date format (use dd.MM.yyyy)";

            if (_workParse)
                return "work";

            _workParse = true;
            string log = "";

            try
            {
                var sw = Stopwatch.StartNew();
                long unixFrom = DateTimeOffset.UtcNow.Date == fromDate.Date
                    ? DateTimeOffset.FromUnixTimeSeconds(0).ToUnixTimeSeconds()
                    : new DateTimeOffset(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

                ParserLog.Write("bitruapi", $"ParseFromDate lastnewtor={lastnewtor} (unix={unixFrom}), limit={limit}");

                var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: unixFrom);
                if (torrents != null && torrents.Count > 0)
                {
                    await SaveTorrentsAndMagnets(torrents);
                    log = $"saved {torrents.Count}";
                }
                else
                    log = "no items";

                ParserLog.Write("bitruapi", $"ParseFromDate completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
            }
            catch (Exception ex)
            {
                ParserLog.Write("bitruapi", $"Error: {ex.Message}");
                log = $"error: {ex.Message}";
            }
            finally
            {
                _workParse = false;
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }

        #endregion

        #region API request + map to TorrentDetails

        /// <summary>Один запрос к API с соблюдением лимита (задержка до запроса вызывается снаружи при необходимости).</summary>
        async Task<BitruApiResponse> ApiRequestAsync(object jsonParams)
        {
            string json = JsonConvert.SerializeObject(jsonParams);
            string postData = $"get={ApiGetTorrents}&json={Uri.EscapeDataString(json)}";
            string response = await HttpClient.Post(ApiUrl, postData, timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            var obj = JsonConvert.DeserializeObject<BitruApiResponse>(response);
            return obj;
        }

        /// <summary>Загрузить торренты из API (один или несколько запросов с пагинацией по before_date).</summary>
        async Task<List<TorrentDetails>> FetchTorrentsFromApi(int limit = 100, long? afterDateUnix = null)
        {
            var all = new List<TorrentDetails>();
            var currentParams = new Dictionary<string, object>
            {
                { "limit", limit },
                { "category", new[] { "movie", "serial" } }
            };
            if (afterDateUnix.HasValue)
                currentParams["after_date"] = afterDateUnix.Value.ToString();

            for (int page = 0; page < 50; page++)
            {
                await Task.Delay(ApiDelayMs);

                var resp = await ApiRequestAsync(currentParams);
                if (resp == null || resp.Error || resp.Result?.Items == null)
                {
                    if (resp != null && resp.Error && !string.IsNullOrEmpty(resp.Message))
                        ParserLog.Write("bitruapi", $"API error: {resp.Message}");
                    break;
                }

                foreach (var wrap in resp.Result.Items)
                {
                    if (wrap?.Item == null)
                        continue;
                    var t = MapToTorrentDetails(wrap.Item);
                    if (t != null)
                        all.Add(t);
                }

                if (resp.Result.Items.Count == 0)
                    break;

                object nextDate = resp.Result.BeforeDate;
                if (nextDate == null)
                    break;

                long beforeUnix = 0;
                if (nextDate is long l)
                    beforeUnix = l;
                else if (nextDate is string s && long.TryParse(s, out long parsed))
                    beforeUnix = parsed;

                if (beforeUnix == 0)
                    break;

                currentParams = new Dictionary<string, object>
                {
                    { "limit", limit },
                    { "category", new[] { "movie", "serial" } },
                    { "before_date", beforeUnix.ToString() }
                };
            }

            return all;
        }

        TorrentDetails MapToTorrentDetails(BitruApiItemInner item)
        {
            var torrent = item.Torrent;
            var info = item.Info;
            var template = item.Template;

            if (torrent == null || info == null || template == null)
                return null;

            string category = (template.Category ?? "").ToLowerInvariant();
            string[] types = null;
            if (category == "movie")
                types = new[] { "movie" };
            else if (category == "serial")
                types = new[] { "serial" };
            else
                return null;

            string name = CleanTitleForSearch(info.Name ?? "")?.Trim();
            string originalname = CleanTitleForSearch(template.OrigName ?? "")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = (info.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(originalname)) originalname = (template.OrigName ?? "").Trim();
            string yearDisplay = BitruYearToDisplayString(info.Year);
            int relased = BitruYearToReleased(info.Year);

            // Для title используем исходные значения из API (с сезоном и т.д.)
            string nameRaw = (info.Name ?? "").Trim();
            string originalnameRaw = (template.OrigName ?? "").Trim();
            string titlePart = nameRaw;
            if (!string.IsNullOrWhiteSpace(originalnameRaw))
                titlePart += " / " + originalnameRaw;
            if (!string.IsNullOrEmpty(yearDisplay))
                titlePart += " (" + yearDisplay + ")";
            if (template.Video?.Quality != null)
                titlePart += " " + template.Video.Quality;
            if (!string.IsNullOrWhiteSpace(template.Other))
                titlePart += " | " + template.Other;

            string url = $"{HostUrl}/details.php?id={torrent.Id}";
            string sizeName = FormatSize(torrent.Size);
            DateTime createTime = DateTimeOffset.FromUnixTimeSeconds(torrent.Added).UtcDateTime;

            return new TorrentDetails
            {
                trackerName = "bitru",
                types = types,
                url = url,
                title = HttpUtility.HtmlDecode(titlePart.Trim()),
                sid = torrent.Seeders,
                pir = torrent.Leechers,
                sizeName = sizeName,
                createTime = createTime,
                name = name.Trim(),
                originalname = originalname?.Trim(),
                relased = relased,
                _sn = torrent.File
            };
        }

        /// <summary>
        /// Убирает из названия сезон, эпизод, качество и т.д. — для name/originalname.
        /// API v2 ищет по базовому имени; сезон указывается отдельным параметром season.
        /// Публичный для использования в DevController.FixBitruNames.
        /// </summary>
        public static string CleanTitleForSearch(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            string t = title.Trim();

            // Год в скобках — убираем и всё после
            var yearMatch = Regex.Match(t, @"[\(\[](\d{4})[\)\]]");
            if (yearMatch.Success && yearMatch.Index > 0)
                t = t.Substring(0, yearMatch.Index);

            // Сезон/эпизод: S01E01, 1x01, 1 сезон, 1-5 сезон
            t = Regex.Replace(t, @"\b(S\d{1,2}E\d{1,2}|S\d{1,2}E?\d{0,2}|E\d{1,2}|\d{1,2}x\d{1,2})\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s*\d{1,2}(-\d{1,2})?\s*сезон\s*.*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(Сезон|Season)\s*\d{1,2}(?!\d).*$", "", RegexOptions.IgnoreCase);

            // Качество и теги релиза
            t = Regex.Replace(t, @"\b(2160p|1080p|720p|480p)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(WEB[-\s]?DL|WEB[-\s]?Rip|BDRip|BDRemux|HDRip|BluRay|BRRip|DVDRip|HDTV)\b", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\b(x264|x265|h\.?264|h\.?265|hevc|avc|aac|ac3|dts)\b", "", RegexOptions.IgnoreCase);

            t = Regex.Replace(t, @"[\[\]\|]", " ");
            t = Regex.Replace(t, @"\s{2,}", " ").Trim().TrimEnd(' ', '/', '-', '|');
            t = Regex.Replace(t, @"[.\s]+-\s*[A-Za-z0-9][A-Za-z0-9.-]*$", "", RegexOptions.IgnoreCase);
            return t.Trim().TrimEnd(' ', '-');
        }

        /// <summary>Год из API может быть number (2020) или string ("2011-2015"). Для отображения в title — как есть.</summary>
        static string BitruYearToDisplayString(object year)
        {
            if (year == null) return "";
            if (year is long l) return l.ToString();
            if (year is int i) return i.ToString();
            return year.ToString()?.Trim() ?? "";
        }

        /// <summary>Год для relased (int): из диапазона "2011-2015" берётся первый год, иначе парсится число.</summary>
        static int BitruYearToReleased(object year)
        {
            if (year == null) return 0;
            if (year is long l) return (int)l;
            if (year is int i) return i;
            var s = year.ToString()?.Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            var dash = s.IndexOf('-');
            var firstPart = dash > 0 ? s.Substring(0, dash).Trim() : s;
            return int.TryParse(firstPart, NumberStyles.None, CultureInfo.InvariantCulture, out int y) ? y : 0;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1000L * 1024)
                return $"{bytes / 1024.0:F2} КБ";
            if (bytes < 1000L * 1048576)
                return $"{bytes / 1048576.0:F2} МБ";
            if (bytes < 1000L * 1073741824)
                return $"{bytes / 1073741824.0:F2} ГБ";
            return $"{bytes / 1099511627776.0:F2} ТБ";
        }

        #endregion

        #region Save to FileDB + magnets (with rate limit)

        async Task SaveTorrentsAndMagnets(List<TorrentDetails> torrents)
        {
            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                string downloadUrl = t._sn;
                if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var idMatch = System.Text.RegularExpressions.Regex.Match(t.url ?? "", @"\?id=(\d+)");
                    downloadUrl = idMatch.Success ? $"{HostUrl}/download.php?id={idMatch.Groups[1].Value}" : null;
                }
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return false;

                await Task.Delay(ApiDelayMs);

                byte[] data = await HttpClient.Download(downloadUrl, referer: HostUrl + "/", timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
                string magnet = data != null ? BencodeTo.Magnet(data) : null;
                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    t.magnet = magnet;
                    t._sn = null;
                    return true;
                }

                return false;
            });

            try
            {
                var lastTor = torrents.OrderByDescending(x => x.createTime).FirstOrDefault();
                if (lastTor != null)
                    IO.File.WriteAllText(LastNewTorPath, lastTor.createTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            }
            catch { }
        }

        #endregion
    }
}

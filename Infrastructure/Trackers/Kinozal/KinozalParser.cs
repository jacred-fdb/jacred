using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;

namespace JacRed.Infrastructure.Trackers.Kinozal
{
    public static class KinozalParser
    {
        const string TrackerName = "kinozal";

        // Chromium/FlareSolverr re-serializes class='first bg' / class=bg as class="first bg" / class="bg".
        const string AttrQ = "[\"']?";
        static readonly Regex RowSplit = new Regex(
            $"<tr class={AttrQ}(?:first )?bg{AttrQ}>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // `/details.php` only — `userdetails.php?id=` contains the substring `details.php?id=`.
        static readonly Regex TorrentListingHref = new Regex(
            @"href=[""']/details\.php\?id=\d+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex NamTorrentHref = new Regex(
            $@"<td class={AttrQ}nam{AttrQ}>\s*<a href=[""']/details\.php\?id=(\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex DetailsIdInUrl = new Regex(
            @"/details\.php\?id=(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex HtmlTitle = new Regex(
            @"<title>([^<]+)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>Digit immediately before <c>rel="next"</c> — 1-based last listing page.</summary>
        static readonly Regex PagerDigitBeforeNext = new Regex(
            @">([0-9]+)</a></li><li><a rel=""next""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex BrowseSelect = new Regex(
            @"<select\s+name=[""']?(c|d)[""']?[^>]*>(.*?)</select>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex BrowseOption = new Regex(
            @"<option([^>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex BrowseOptionValue = new Regex(
            @"value=[""']?(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ArgYear = new Regex(
            @"(?:^|[?&])d=(\d{4})(?:&|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parse browse-list date column (header «Залит»).
        /// Kinozal shows Обновлен when torrent was re-uploaded; otherwise shows Залит (upload only).
        /// Formats: сегодня/вчера в HH:mm or dd.MM.yyyy в HH:mm.
        /// </summary>
        public static DateTime ParseListingUpdateTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return default;

            raw = Regex.Replace(raw.Trim(), "[\n\r\t ]+", " ");

            var relative = Regex.Match(raw, "^(сегодня|вчера) в ([0-9]{2}):([0-9]{2})$", RegexOptions.IgnoreCase);
            if (relative.Success)
            {
                var baseDate = string.Equals(relative.Groups[1].Value, "сегодня", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow.Date
                    : DateTime.UtcNow.Date.AddDays(-1);

                int hour = int.Parse(relative.Groups[2].Value);
                int minute = int.Parse(relative.Groups[3].Value);
                return baseDate.AddHours(hour).AddMinutes(minute);
            }

            var absolute = Regex.Match(raw, "^([0-9]{2})\\.([0-9]{2})\\.([0-9]{4}) в ([0-9]{2}):([0-9]{2})$");
            if (absolute.Success)
            {
                return new DateTime(
                    int.Parse(absolute.Groups[3].Value),
                    int.Parse(absolute.Groups[2].Value),
                    int.Parse(absolute.Groups[1].Value),
                    int.Parse(absolute.Groups[4].Value),
                    int.Parse(absolute.Groups[5].Value),
                    0,
                    DateTimeKind.Utc);
            }

            return tParse.ParseCreateTime(raw, "dd.MM.yyyy");
        }

        public static List<TorrentDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentDetails>();

            if (!KinozalCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            foreach (string row in RowSplit.Split(tParse.ReplaceBadNames(html)).Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Дата создания
                string listingTime = Match($"<td class={AttrQ}sl_p{AttrQ}>[0-9]+</td>\\s*<td class={AttrQ}s{AttrQ}>([^<]+)</td>");
                DateTime createTime = ParseListingUpdateTime(listingTime);

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                if (!TryGetDetailsIdFromRow(row, out int detailsId))
                    continue;

                string title = Match($"class={AttrQ}r[0-9]+{AttrQ}>([^<]+)</a>");
                string _sid = Match($"<td class={AttrQ}sl_s{AttrQ}>([0-9]+)</td>");
                string _pir = Match($"<td class={AttrQ}sl_p{AttrQ}>([0-9]+)</td>");
                string sizeName = Match($"<td class={AttrQ}s{AttrQ}>([0-9\\.,]+ (МБ|ГБ|ТБ))</td>");

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                string url = DetailsUrl(AppInit.conf.Kinozal.host, detailsId);
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                switch (meta.TitleKind)
                {
                    case KinozalTitleKind.Movie:
                        ParseMovieTitle(title, out name, out originalname, out relased);
                        break;
                    case KinozalTitleKind.SerialRu:
                        ParseSerialRuTitle(title, row, out name, out relased);
                        break;
                    case KinozalTitleKind.SerialEn:
                        ParseSerialEnTitle(title, row, out name, out originalname, out relased);
                        break;
                    case KinozalTitleKind.TvShow:
                        ParseTvShowTitle(title, out name, out originalname, out relased);
                        break;
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentDetails()
                {
                    trackerName = TrackerName,
                    types = meta.Types,
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }

            return torrents;
        }

        static void ParseMovieTitle(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            // Бэд трип (Приколисты в дороге) / Bad Trip / 2020 / ДБ, СТ / WEB-DLRip (AVC)
            // Интерстеллар / Interstellar (IMAX Edition) / 2014 / ДБ / BDRip
            // Успеть всё за месяц / 30 jours max / 2020 / ЛМ / WEB-DLRip
            var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
            {
                name = g[1].Value;
                originalname = g[3].Value;

                if (int.TryParse(g[5].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                // Name may contain parentheses and season-like slashes (RU-only titles):
                // Голая правда / 2020 / ЛМ / WEB-DLRip
                // Some listings: Title (note) / 2020 / РУ / WEB-DL (1080p)
                var yearField = Regex.Match(title, " / ((?:19|20)[0-9]{2}) / ");
                if (yearField.Success)
                {
                    name = title.Substring(0, yearField.Index).Trim();
                    if (int.TryParse(yearField.Groups[1].Value, out int _yer))
                        relased = _yer;
                }
            }
        }

        static void ParseSerialRuTitle(string title, string row, out string name, out int relased)
        {
            name = null;
            relased = 0;

            if (row.Contains("сезон"))
            {
                // Сельский детектив (6 сезон: 1-2 серии из 2) ([^/]+)?/ 2020 / РУ / WEB-DLRip (AVC)
                // Любовь в рабочие недели (1 сезон: 1 серия из 15) / 2020 / РУ / WEB-DLRip (AVC)
                // Фитнес (Королева фитнеса) (1-4 сезон: 1-80 серии из 80) / 2018-2020 / РУ / WEB-DLRip
                // Бывшие (1-3 сезон: 1-24 серии из 24) / 2016-2020 / РУ / WEB-DLRip (AVC)
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                {
                    name = g[1].Value;

                    if (int.TryParse(g[4].Value, out int _yer))
                        relased = _yer;
                }
            }
            else
            {
                // Авантюра на двоих (1-8 серии из 8) / 2021 / РУ /  WEBRip (AVC)
                // Жизнь после жизни (Небеса подождут) (1-16 серии из 16) / 2016 / РУ / WEB-DLRip
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;

                name = g[1].Value;
                if (int.TryParse(g[4].Value, out int _yer))
                    relased = _yer;
            }
        }

        static void ParseSerialEnTitle(string title, string row, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            if (row.Contains("сезон"))
            {
                // Сокол и Зимний солдат (1 сезон: 1-2 серия из 6) / The Falcon and the Winter Soldier / 2021 / ЛД (#NW), СТ / WEB-DL (1080p)
                // Голубая кровь (Семейная традиция) (11 сезон: 1-9 серия из 20) / Blue Bloods / 2020 / ПМ (BaibaKo) / WEBRip
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                {
                    name = g[1].Value;
                    originalname = g[4].Value;

                    if (int.TryParse(g[5].Value, out int _yer))
                        relased = _yer;
                }
            }
            else
            {
                // Дикий ангел (151-270 серии из 270) / Muneca Brava / 1998-1999 / ПМ / DVB
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                {
                    name = g[1].Value;
                    originalname = g[4].Value;

                    if (int.TryParse(g[5].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^\\(/]+) / ([^\\(/]+) / ([0-9]{4})").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
            }
        }

        static void ParseTvShowTitle(string title, out string name, out string originalname, out int relased)
        {
            name = null;
            originalname = null;
            relased = 0;

            // Топ Гир (30 сезон: 1-2 выпуски из 10) / Top Gear / 2021 / ЛМ (ColdFilm) / WEBRip
            var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
            {
                name = g[1].Value;
                originalname = g[3].Value;

                if (int.TryParse(g[4].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                // Супермама (3 сезон: 1-12 выпуски из 40) / 2021 / РУ / IPTV (1080p)
                g = Regex.Match(title, "^([^/\\(]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})").Groups;

                name = g[1].Value;
                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }
        }

        /// <summary>
        /// Info hash from <c>get_srv_details.php?id=&amp;action=2</c>.
        /// FlareSolverr GET returns a short UTF-8 fragment with «Инфо хеш».
        /// Ignore browse-sized HTML (stale Chromium tab) so we do not mint a fake magnet.
        /// </summary>
        public static string ParseInfoHash(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var labeled = Regex.Match(html, "<ul><li>Инфо хеш:\\s*([A-Fa-f0-9]{40})</li>");
            if (labeled.Success)
                return labeled.Groups[1].Value;

            if (html.Length > 8000)
                return null;

            var loose = Regex.Match(html, "([A-Fa-f0-9]{40})");
            return loose.Success ? loose.Groups[1].Value : null;
        }

        public static bool IsLoggedIn(string html) =>
            !string.IsNullOrEmpty(html) && html.Contains(">Выход</a>");

        /// <summary>
        /// Null, nginx 503 behind Cloudflare, or a CF interstitial — retry, do not TakeLogin.
        /// </summary>
        public static bool IsTransientBrowseFailure(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return true;

            if (html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || html.Contains("Один момент", StringComparison.OrdinalIgnoreCase))
                return true;

            return html.Length < 2000
                && html.Contains("503 Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLoginWall(string html)
        {
            if (string.IsNullOrWhiteSpace(html) || IsTransientBrowseFailure(html) || IsLoggedIn(html))
                return false;

            return html.Contains("takelogin.php", StringComparison.OrdinalIgnoreCase)
                || html.Contains("take_login", StringComparison.OrdinalIgnoreCase)
                || html.Contains("name=\"username\"", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Torrent id from a details URL. Returns false for <c>userdetails.php</c> profile links.
        /// </summary>
        public static bool TryGetDetailsId(string url, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(url))
                return false;

            if (url.IndexOf("userdetails", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            var match = DetailsIdInUrl.Match(url);
            return match.Success && int.TryParse(match.Groups[1].Value, out id) && id > 0;
        }

        public static string DetailsUrl(string host, int id)
        {
            string baseHost = string.IsNullOrWhiteSpace(host) ? "https://kinozal.guru" : host.TrimEnd('/');
            return $"{baseHost}/details.php?id={id}";
        }

        static bool TryGetDetailsIdFromRow(string row, out int id)
        {
            id = 0;
            var nam = NamTorrentHref.Match(row);
            if (nam.Success && int.TryParse(nam.Groups[1].Value, out id) && id > 0)
                return true;

            var href = TorrentListingHref.Match(row);
            return href.Success && TryGetDetailsId(href.Value, out id);
        }

        public static int CountTorrentListingLinks(string html)
        {
            if (string.IsNullOrEmpty(html))
                return 0;

            return TorrentListingHref.Matches(html).Count;
        }

        public static bool HasTorrentListingLinks(string html) =>
            CountTorrentListingLinks(html) > 0;

        static bool HasKinozalTitle(string html)
        {
            if (string.IsNullOrEmpty(html))
                return false;

            var title = HtmlTitle.Match(html);
            if (title.Success && title.Groups[1].Value.Contains("Кинозал", StringComparison.OrdinalIgnoreCase))
                return true;

            return html.Contains("Кинозал.GURU", StringComparison.Ordinal)
                || html.Contains("Кинозал.ТВ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Real browse table (header present). Does not require torrent rows — empty categories are valid.
        /// Does not treat <c>userdetails.php?id=</c> as a listing.
        /// </summary>
        public static bool IsValidBrowsePage(string html) =>
            !string.IsNullOrWhiteSpace(html)
            && html.Contains("t_peer", StringComparison.Ordinal)
            && HasKinozalTitle(html);

        /// <summary>
        /// Year filter / past last listing: logged-in chrome, no table,
        /// «Нет активных раздач». Mark ParseAll done; do not recycle.
        /// «уточните параметры поиска» also appears on listings over 5000 hits — do not use it alone.
        /// </summary>
        public static bool IsEmptySearchResult(string html)
        {
            if (IsTransientBrowseFailure(html) || IsLoginWall(html))
                return false;

            if (html.Contains("t_peer", StringComparison.Ordinal))
                return false;

            if (!IsLoggedIn(html) || !HasKinozalTitle(html))
                return false;

            return html.Contains("Нет активных раздач", StringComparison.Ordinal);
        }

        /// <summary>
        /// Selected option of browse <c>select name=c|d</c>. No form → false (not a leftover tab).
        /// </summary>
        public static bool TryGetSelectedBrowseFilter(string html, string name, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(name))
                return false;

            foreach (Match select in BrowseSelect.Matches(html))
            {
                if (!string.Equals(select.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (Match option in BrowseOption.Matches(select.Groups[2].Value))
                {
                    string attrs = option.Groups[1].Value;
                    if (attrs.IndexOf("selected", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var val = BrowseOptionValue.Match(attrs);
                    if (!val.Success)
                        return false;

                    value = val.Groups[1].Value;
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetRequestedYear(string arg, out string year)
        {
            year = null;
            if (string.IsNullOrEmpty(arg))
                return false;

            var match = ArgYear.Match(arg);
            if (!match.Success)
                return false;

            year = match.Groups[1].Value;
            return true;
        }

        /// <summary>
        /// FlareSolverr leftover tab: listing/empty HTML for another category or year.
        /// No form fields → not a mismatch (transient/stale). Selected year 0 (все года) is not a mismatch.
        /// Hourly parse (<paramref name="arg"/> null) checks category only.
        /// </summary>
        public static bool BrowseFiltersMismatch(string html, string cat, string arg)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(cat))
                return false;

            if (TryGetSelectedBrowseFilter(html, "c", out string selectedCat)
                && !string.Equals(selectedCat, cat, StringComparison.Ordinal))
                return true;

            if (TryGetRequestedYear(arg, out string year)
                && TryGetSelectedBrowseFilter(html, "d", out string selectedYear)
                && selectedYear != "0"
                && !string.Equals(selectedYear, year, StringComparison.Ordinal))
                return true;

            return false;
        }

        /// <summary>
        /// Digit before <c>rel="next"</c> is 1-based last listing page. URL <c>page</c> is 0-based.
        /// No pager → one page (index 0). Loop <c>for (page = 0; page &lt; count; page++)</c>.
        /// </summary>
        public static int YearTaskPageCount(int pagerDigitBeforeNext)
        {
            if (pagerDigitBeforeNext <= 0)
                return 1;

            return pagerDigitBeforeNext;
        }

        public static int YearTaskPageCount(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return 1;

            var match = PagerDigitBeforeNext.Match(html);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int digit))
                return 1;

            return YearTaskPageCount(digit);
        }

        /// <summary>
        /// Drop URL pages at or past the last listing page (old inclusive <c>page &lt;= digit</c> tails).
        /// </summary>
        public static int PrunePagesBeyondYearCount(List<TaskParse> tasks, int pageCount)
        {
            if (tasks == null || tasks.Count == 0)
                return 0;

            if (pageCount < 1)
                pageCount = 1;

            int before = tasks.Count;
            tasks.RemoveAll(t => t != null && t.page >= pageCount);
            return before - tasks.Count;
        }

        /// <summary>
        /// Length / t_peer / title for stale logs. No cookies, no HTML body.
        /// </summary>
        public static string FormatBrowseDiag(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "len=0";

            var title = HtmlTitle.Match(html);
            string t = title.Success ? title.Groups[1].Value.Trim() : "";
            if (t.Length > 80)
                t = t.Substring(0, 80);

            return $"len={html.Length} t_peer={html.Contains("t_peer", StringComparison.Ordinal)} title={t}";
        }

        /// <summary>
        /// UpdateTasksParse year-page delay. Cap so 25 cats × ~37 years still finish inside the 2h wall clock.
        /// </summary>
        internal static int UpdateTasksParseDelayMs(int parseDelay) =>
            Math.Clamp(parseDelay, 0, 2000);

        /// <summary>
        /// Logged-in Kinozal chrome without a <c>t_peer</c> table — typical ~15 KB FlareSolverr empty tab.
        /// Retry; do not TakeLogin and do not mark ParseAllTask done.
        /// Empty search («Нет активных раздач») is not stale — see <see cref="IsEmptySearchResult"/>.
        /// </summary>
        public static bool IsStaleListingHtml(string html)
        {
            if (IsTransientBrowseFailure(html) || IsLoginWall(html) || IsEmptySearchResult(html))
                return false;

            if (html.Contains("t_peer", StringComparison.Ordinal))
                return false;

            return IsLoggedIn(html);
        }

        /// <summary>
        /// Empty listing (no torrent hrefs) → done. Parser miss (hrefs but 0 rows) → not done.
        /// Rows that still need a magnet → not done until every row is resolved.
        /// </summary>
        public static bool ShouldMarkPageDone(int parsedCount, int resolvedCount, int listingHrefCount)
        {
            if (listingHrefCount > 0 && parsedCount <= 0)
                return false;

            if (parsedCount <= 0)
                return true;

            return resolvedCount >= parsedCount;
        }

        /// <summary>
        /// Кинозал при добавлении серий/озвучек перехеширует .torrent (новый info hash),
        /// но title в списке часто не меняется — раньше hash не перезапрашивался.
        /// createTime = date from browse «Залит» column: Обновлен if present on details,
        /// otherwise original Залит (never re-uploaded).
        /// </summary>
        internal static bool ShouldSkipHashFetch(TorrentDetails cached, TorrentDetails parsed)
        {
            if (string.IsNullOrWhiteSpace(cached.magnet))
                return false;

            if (cached.title != parsed.title)
                return false;

            if (cached.sizeName != parsed.sizeName)
                return false;

            // Обновлен changed (including time on the same day) → rehash likely
            if (parsed.createTime > cached.createTime)
                return false;

            return true;
        }
    }
}

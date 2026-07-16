using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        /// <summary>Строит по HTML карту urlPath сериала (series/.../season_N/episode_M/) -> (name ru, originalname) из блоков hor-breaker, чтобы подставлять русское название в episode_links и избегать дубликатов бакетов. Добавляется и ключ по сериалу (series/Slug), чтобы все эпизоды одного сериала получали одно русское имя (Пони, а не Ponies).</summary>
        public static Dictionary<string, (string name, string originalname)> BuildHorBreakerNameMap(string html)
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (string row in html.Split(new[] { "class=\"hor-breaker dashed\"" }, StringSplitOptions.None).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;
                string url = Regex.Match(row, @"href=""/([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string name = Regex.Match(row, @"<div class=""name-ru"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                string originalname = Regex.Match(row, @"<div class=""name-en"">([^<]+)</div>", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname))
                    continue;
                string key = url.TrimEnd('/');
                var pair = (HttpUtility.HtmlDecode(name), HttpUtility.HtmlDecode(originalname));
                if (!map.ContainsKey(key))
                    map[key] = pair;
                // Ключ по сериалу (series/Slug), чтобы эпизоды, которых нет в hor-breaker на этой странице, тоже получили русское имя (например Пони вместо Ponies).
                var seriesMatch = Regex.Match(url, @"^series/([^/]+)(?:/|$)", RegexOptions.IgnoreCase);
                if (seriesMatch.Success)
                {
                    string seriesKey = "series/" + seriesMatch.Groups[1].Value.TrimEnd('/');
                    if (!map.ContainsKey(seriesKey))
                        map[seriesKey] = pair;
                }
            }
            return map;
        }

        /// <summary>Оставляет по одному торренту на url; при дубликате оставляет запись с русским названием (name != originalname), чтобы ключ бакета был один.</summary>
        public static void DedupeListByUrl(List<TorrentDetails> list)
        {
            var byUrl = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in list)
            {
                if (string.IsNullOrEmpty(t?.url))
                    continue;
                if (!byUrl.TryGetValue(t.url, out var existing))
                {
                    byUrl[t.url] = t;
                    continue;
                }
                // Уже есть запись: оставляем ту, у которой есть русское название (name != originalname)
                bool currentHasRu = HasRuName(t);
                bool existingHasRu = HasRuName(existing);
                if (currentHasRu && !existingHasRu)
                    byUrl[t.url] = t;
            }
            list.Clear();
            list.AddRange(byUrl.Values);
        }

        /// <summary>Извлекает год выхода и русское название из HTML страницы сериала или /seasons/. Без запросов — только парсинг.</summary>
        public static (int year, string russianName) ParseRelasedAndNameFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return (0, null);
            var m = Regex.Match(html, @"itemprop=""dateCreated""\s+content=""(\d{4})-\d{2}-\d{2}""");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int year) || year <= 0)
                return (0, null);
            string russianName = null;
            var og = Regex.Match(html, @"<meta\s+property=""og:title""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
            if (og.Success)
                russianName = HttpUtility.HtmlDecode(og.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(russianName))
            {
                var tit = Regex.Match(html, @"<title>([^<]+?)\.?\s*[–-]\s*LostFilm", RegexOptions.IgnoreCase);
                if (tit.Success)
                    russianName = ShortenSeriesName(HttpUtility.HtmlDecode(tit.Groups[1].Value.Trim()));
            }
            else
                russianName = ShortenSeriesName(russianName);
            return (year, russianName);
        }

        /// <summary>Store 1080p and 2160p from V-pages (episodes, movies, season packs).</summary>
        public static readonly string[] PreferredQualities = { "1080p", "2160p" };

        public static bool IsPreferredQuality(string quality)
        {
            string q = NormalizeQuality(quality);
            if (string.IsNullOrEmpty(q))
                return false;
            foreach (string preferred in PreferredQualities)
            {
                if (string.Equals(q, preferred, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Нормализует качество в единый формат: 1080/720 → 1080p/720p, SD без изменений.</summary>
        public static string NormalizeQuality(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality))
                return quality;
            string q = quality.Trim();
            if (Regex.IsMatch(q, @"^\d{3,4}p$", RegexOptions.IgnoreCase))
                return q.ToLowerInvariant();
            if (string.Equals(q, "1080", StringComparison.OrdinalIgnoreCase))
                return "1080p";
            if (string.Equals(q, "2160", StringComparison.OrdinalIgnoreCase))
                return "2160p";
            if (string.Equals(q, "720", StringComparison.OrdinalIgnoreCase))
                return "720p";
            if (string.Equals(q, "sd", StringComparison.OrdinalIgnoreCase))
                return "SD";
            if (string.Equals(q, "mp4", StringComparison.OrdinalIgnoreCase))
                return "720p";
            return q;
        }

        /// <summary>Извлекает короткое русское название сериала для полей name/title. og:title на LostFilm часто содержит длинный текст: "Название (English). Сериал ... гид по сериям... / OriginalName / N сезон M серия [year, 1080p]".</summary>
        public static string ShortenSeriesName(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return title?.Trim() ?? "";

            const int maxNameLength = 200;
            string s = title.Trim();

            // 1) og:title формат: "Название (English). Сериал Название (English) канал (страны): гид по сериям..." — берём до ". Сериал", затем до " (" (только русское название)
            int idxSer = s.IndexOf(". Сериал", StringComparison.OrdinalIgnoreCase);
            if (idxSer >= 0)
            {
                s = s.Substring(0, idxSer).Trim();
                int idxParen = s.IndexOf(" (", StringComparison.Ordinal);
                if (idxParen >= 0)
                    s = s.Substring(0, idxParen).Trim();
                if (s.Length > 0 && s.Length <= maxNameLength)
                    return s;
            }

            // 2) Уже в формате "Name RU / Name EN / N сезон M серия [year]" или "[year, 1080p]" — извлекаем первый сегмент (русское название)
            var m = Regex.Match(s, @"^(.+?)\s*/\s*[^/]+?\s*/\s*\d+\s*сезон\s*\d+\s*серия\s*\[\d{4}(?:,[^\]]*)?\]\s*$");
            if (m.Success)
            {
                s = m.Groups[1].Value.Trim();
                if (s.Length > 0 && s.Length <= maxNameLength)
                    return s;
            }

            // 3) Есть скобка " (Original Name)" — оставляем только русскую часть
            int idx = s.IndexOf(" (", StringComparison.Ordinal);
            if (idx >= 0)
                s = s.Substring(0, idx).Trim();

            if (s.Length > maxNameLength)
                s = s.Substring(0, maxNameLength).Trim();
            return s.Length > 0 ? s : title.Trim();
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        /// <summary>Извлекает числовой ID раздачи из URL трекера. При обновлении раздачи на трекере меняется slug, но ID остаётся — без этого создавались бы дубликаты.</summary>
        static int GetTorrentIdFromUrl(string trackerName, string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;

            // Rutor: .../torrent/1070749/...
            if (string.Equals(trackerName, "rutor", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"/torrent/(\d+)");
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // TorrentBy: host/{id}/{slug} - ID is first numeric segment after host
            if (string.Equals(trackerName, "torrentby", StringComparison.OrdinalIgnoreCase))
            {
                // Extract path after host, then get first numeric segment
                var pathMatch = Regex.Match(url, @"https?://[^/]+/(\d+)/");
                if (pathMatch.Success && int.TryParse(pathMatch.Groups[1].Value, out int id))
                    return id;

                // Fallback: match any numeric segment at start of path
                var m = Regex.Match(url, @"/(\d+)/");
                return m.Success && int.TryParse(m.Groups[1].Value, out int id2) ? id2 : 0;
            }

            // Megapeer: .../torrent/{id}/{slug} - по комментарию в коде может быть slug
            if (string.Equals(trackerName, "megapeer", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"/torrent/(\d+)");
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Selezen: .../relizy-ot-selezen/12292-slug-name.html — ID перед первым дефисом
            if (string.Equals(trackerName, "selezen", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"/relizy-ot-selezen/(\d+)-");
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Baibako: details.php?id=42075 или /details.php?id=42075
            if (string.Equals(trackerName, "baibako", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"details\.php\?id=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Kinozal: .../details.php?id=2058877 (домен мог смениться .tv → .guru)
            if (string.Equals(trackerName, "kinozal", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"details\.php\?id=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // NNMClub: .../forum/viewtopic.php?t=1882070
            if (string.Equals(trackerName, "nnmclub", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"viewtopic\.php\?t=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Anibelka: .../viewtopic.php?t=1849
            if (string.Equals(trackerName, "anibelka", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"viewtopic\.php\?t=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Korsars: .../viewtopic.php?t=12345
            if (string.Equals(trackerName, "korsars", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"viewtopic\.php\?t=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Lostfilm: host-independent path + #quality (qualities stay distinct).
            if (string.Equals(trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase))
                return JacRed.Infrastructure.Trackers.Lostfilm.LostfilmParser.StableUrlId(url);

            // Anistar: .../slug.html?e=3&id=1001 (torrent block id)
            if (string.Equals(trackerName, "anistar", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Leproduction: .../slug.html?q=1080&id=12345
            if (string.Equals(trackerName, "leproduction", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            // Viruseproject: .../post#q=1080&id=13548
            if (string.Equals(trackerName, "viruseproject", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(url, @"[#?&]id=(\d+)", RegexOptions.IgnoreCase);
                return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
            }

            return 0;
        }

    }
}

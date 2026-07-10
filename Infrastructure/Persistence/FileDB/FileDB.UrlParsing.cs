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

            return 0;
        }

    }
}

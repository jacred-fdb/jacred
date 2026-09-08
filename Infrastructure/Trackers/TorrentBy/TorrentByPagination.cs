using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Trackers.TorrentBy
{
    sealed class TorrentByPager
    {
        public int CurrentDisplay { get; init; }
        public int MaxPageIndex { get; init; }
        public bool HasTrailingEllipsis { get; init; }
        public int? EllipsisJumpPage { get; init; }
    }

    /// <summary>
    /// torrent.by listing pager: 0-based <c>?page=</c>, chips in <c>circle_page</c> spans,
    /// last chip often <c>...</c> (not the true last page).
    /// </summary>
    static class TorrentByPagination
    {
        public const int MaxEllipsisHops = 40;

        /// <summary>Pre-circle_page UpdateTasksParse regex. Does not match live HTML.</summary>
        public const string LegacyMaxPageRegex =
            "href=\"\\?page=([0-9]+)\">[0-9]+</a>([\\t ]+)?</center></td>";

        static readonly Regex PagerBlockRe = new(
            @"Страницы:(.*?)</center>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex PageHrefRe = new(
            @"href=""\?page=([0-9]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex CircleChipRe = new(
            @"<span class=""circle_page""[^>]*>([^<]*)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex CurrentChipRe = new(
            @"<span class=""circle_page""[^>]*background[^>]*>([0-9]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex PageLinkRe = new(
            @"<a[^>]*href=""\?page=([0-9]+)""[^>]*>\s*<span class=""circle_page""[^>]*>([^<]*)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static TorrentByPager ParsePager(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return Empty();

            var blockMatch = PagerBlockRe.Match(html);
            string block = blockMatch.Success ? blockMatch.Groups[1].Value : "";
            if (string.IsNullOrWhiteSpace(block))
                return Empty();

            int maxHref = 0;
            bool anyHref = false;
            foreach (Match m in PageHrefRe.Matches(block))
            {
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    continue;
                anyHref = true;
                if (n > maxHref)
                    maxHref = n;
            }

            int currentDisplay = 1;
            var current = CurrentChipRe.Match(block);
            if (current.Success
                && int.TryParse(current.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cur))
            {
                currentDisplay = cur;
            }
            else
            {
                var first = CircleChipRe.Match(block);
                if (first.Success
                    && int.TryParse(first.Groups[1].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int firstN))
                {
                    currentDisplay = firstN;
                }
            }

            var chips = CircleChipRe.Matches(block);
            string lastChip = chips.Count > 0 ? chips[^1].Groups[1].Value.Trim() : "";
            bool ellipsis = lastChip is "..." or "…";

            int? jump = null;
            if (ellipsis)
            {
                Match lastLink = null;
                foreach (Match m in PageLinkRe.Matches(block))
                    lastLink = m;
                if (lastLink != null
                    && int.TryParse(lastLink.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int j))
                    jump = j;
                else if (anyHref)
                    jump = maxHref;
            }

            int currentIndex = Math.Max(0, currentDisplay - 1);
            int maxPageIndex = Math.Max(currentIndex, anyHref ? maxHref : 0);

            return new TorrentByPager
            {
                CurrentDisplay = currentDisplay,
                MaxPageIndex = maxPageIndex,
                HasTrailingEllipsis = ellipsis,
                EllipsisJumpPage = jump
            };
        }

        static TorrentByPager Empty() => new()
        {
            CurrentDisplay = 1,
            MaxPageIndex = 0,
            HasTrailingEllipsis = false,
            EllipsisJumpPage = null
        };
    }
}

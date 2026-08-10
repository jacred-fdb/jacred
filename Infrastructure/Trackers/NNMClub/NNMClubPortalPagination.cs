using System;
using System.Collections.Generic;
using JacRed.Models.tParse;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    /// <summary>
    /// NNMClub portal only serves a limited page window (~500). Older offsets redirect to FAQ topic
    /// https://nnmclub.to/forum/viewtopic.php?t=1626984
    /// </summary>
    public static class NNMClubPortalPagination
    {
        /// <summary>Max zero-based portal pages to keep in taskParse (pages 0..MaxPortalPages-1).</summary>
        public const int MaxPortalPages = 500;

        /// <summary>FAQ topic that explains the portal page limit.</summary>
        public const string PortalLimitFaqTopicId = "1626984";

        public enum PageParseStatus
        {
            /// <summary>HTTP/HTML failure — retry later.</summary>
            TransientError,

            /// <summary>Redirected to portal-limit FAQ — settle for today.</summary>
            PortalLimitFaq,

            /// <summary>Valid portal listing with zero torrents — settle for today.</summary>
            EmptyPortal,

            /// <summary>Parsed at least one torrent.</summary>
            OkWithTorrents
        }

        /// <summary>
        /// Pager digit before «След.» is 1-based last page; legacy loop used page &lt;= maxpages.
        /// Clamp that inclusive count to <see cref="MaxPortalPages"/>.
        /// </summary>
        public static int ClampTaskPageCount(int maxpagesFromPager)
        {
            if (maxpagesFromPager < 0)
                maxpagesFromPager = 0;

            long inclusive = (long)maxpagesFromPager + 1;
            if (inclusive > MaxPortalPages)
                return MaxPortalPages;

            return (int)inclusive;
        }

        public static bool IsPortalLimitFaq(string html)
        {
            if (string.IsNullOrEmpty(html))
                return false;

            if (html.Contains($"t={PortalLimitFaqTopicId}", StringComparison.Ordinal)
                || html.Contains($"viewtopic.php?t={PortalLimitFaqTopicId}", StringComparison.Ordinal))
                return true;

            if (html.Contains("только 500 страниц", StringComparison.OrdinalIgnoreCase))
                return true;

            if (html.Contains("500 страниц", StringComparison.OrdinalIgnoreCase)
                && html.Contains("<title>", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool LooksLikePortalListing(string html) =>
            !string.IsNullOrEmpty(html) && html.Contains("paginport", StringComparison.Ordinal);

        /// <summary>Remove tasks with page &gt;= <see cref="MaxPortalPages"/>. Returns removed count.</summary>
        public static int PruneTasksBeyondPortalLimit(List<TaskParse> tasks)
        {
            if (tasks == null || tasks.Count == 0)
                return 0;

            int before = tasks.Count;
            tasks.RemoveAll(t => t != null && t.page >= MaxPortalPages);
            return before - tasks.Count;
        }

        public static PageParseStatus ClassifyPage(string html, int torrentCount)
        {
            if (html == null || !html.Contains("NNM-Club</title>", StringComparison.Ordinal))
                return PageParseStatus.TransientError;

            if (IsPortalLimitFaq(html))
                return PageParseStatus.PortalLimitFaq;

            if (torrentCount > 0)
                return PageParseStatus.OkWithTorrents;

            if (LooksLikePortalListing(html))
                return PageParseStatus.EmptyPortal;

            return PageParseStatus.TransientError;
        }

        public static bool ShouldSettleTask(PageParseStatus status) =>
            status == PageParseStatus.OkWithTorrents
            || status == PageParseStatus.PortalLimitFaq
            || status == PageParseStatus.EmptyPortal;
    }
}

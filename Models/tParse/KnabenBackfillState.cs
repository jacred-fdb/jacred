using System;
using System.Collections.Generic;

namespace JacRed.Models.tParse
{
    /// <summary>Checkpoint for Knaben archive backfill (Data/temp/knaben_backfill.json).</summary>
    public class KnabenBackfillState
    {
        public int CategoryIndex { get; set; }

        public int CategoryId { get; set; }

        /// <summary>asc | desc</summary>
        public string Direction { get; set; } = "asc";

        public int From { get; set; }

        /// <summary>categoryId → pending | complete | partial</summary>
        public Dictionary<string, string> CategoryStatus { get; set; } = new Dictionary<string, string>();

        /// <summary>Knaben hit IDs from the last asc page (inner edge of the old window).</summary>
        public List<string> AscEdgeIds { get; set; } = new List<string>();

        /// <summary>True if any desc-page ID intersected AscEdgeIds during the current category.</summary>
        public bool DescSawOverlap { get; set; }

        public bool Finished { get; set; }

        public int TotalFetched { get; set; }

        public int TotalAdded { get; set; }

        public int TotalUpdated { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}

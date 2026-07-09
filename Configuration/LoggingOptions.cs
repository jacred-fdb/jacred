using System.Collections.Generic;

namespace JacRed.Configuration
{
    /// <summary>Optional logging tuning (init.yaml logging: block). Absent = legacy console behavior.</summary>
    public class LoggingOptions
    {
        public string defaultLevel = "Information";

        public bool consoleTimestamp = false;

        /// <summary>Per-category minimum level: tracks, sync, syncSpidr, cron, fdb, stats, trackers, config.</summary>
        public Dictionary<string, string> categories;

        /// <summary>When false, verbose per-torrent tracks steps go to Debug (hidden unless Debug enabled).</summary>
        public bool tracksConsoleDetail = false;

        /// <summary>HTTP /cron/ responses faster than this (ms) with status 200 log at Debug. 0 = log all.</summary>
        public int cronSkipFastMs = 100;
    }
}

using System;
using System.Collections.Generic;

namespace JacRed.Infrastructure.Tracks
{
    internal sealed class TracksIndexFile
    {
        public DateTime builtAt { get; set; }
        public List<string> hashes { get; set; }
    }

    public class TracksStatsCacheFile
    {
        public DateTime updatedAt { get; set; }
        public List<TracksStatsCacheEntry> entries { get; set; } = new List<TracksStatsCacheEntry>();
    }

    public class TracksStatsCacheEntry
    {
        public bool includeTorrentDb { get; set; }
        public TracksExportStats stats { get; set; }
    }

    public class TracksExportStats
    {
        public int total { get; set; }
        public int filesScanned { get; set; }
        public int fromTracksFiles { get; set; }
        public int fromMemory { get; set; }
        public int fromTorrentDb { get; set; }
        public int torrentsScanned { get; set; }
        public int invalidPath { get; set; }
        public int emptyStreams { get; set; }
        public int readErrors { get; set; }
        public int magnetErrors { get; set; }
        public int torrentDbErrors { get; set; }
    }

    public class TracksExportResult
    {
        public string outputDir { get; set; }
        public bool dryRun { get; set; }
        public bool includeTorrentDb { get; set; }
        public TracksExportStats stats { get; set; }
        public int written { get; set; }
        public int writeErrors { get; set; }
        public List<object> errorSamples { get; set; } = new List<object>();
    }

    public class TracksExportJobStatus
    {
        public bool running { get; set; }
        public string phase { get; set; }
        public string outputDir { get; set; }
        public bool includeTorrentDb { get; set; }
        public DateTime? startedAt { get; set; }
        public DateTime? completedAt { get; set; }
        public int total { get; set; }
        public int written { get; set; }
        public int writeErrors { get; set; }
        public TracksExportStats stats { get; set; }
        public TracksExportResult result { get; set; }
        public string error { get; set; }

        public TracksExportJobStatus Clone()
        {
            return new TracksExportJobStatus
            {
                running = running,
                phase = phase,
                outputDir = outputDir,
                includeTorrentDb = includeTorrentDb,
                startedAt = startedAt,
                completedAt = completedAt,
                total = total,
                written = written,
                writeErrors = writeErrors,
                stats = stats,
                result = result,
                error = error
            };
        }
    }

    public class TracksBackfillResult
    {
        public string tracksDir { get; set; }
        public bool dryRun { get; set; }
        public bool includeTorrentDb { get; set; }
        public bool migrateLegacy { get; set; }
        public TracksExportStats stats { get; set; }
        public int written { get; set; }
        public int migratedLegacy { get; set; }
        public int skippedExisting { get; set; }
        public int writeErrors { get; set; }
        public List<object> errorSamples { get; set; } = new List<object>();
    }
}

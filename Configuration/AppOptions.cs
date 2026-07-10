using JacRed.Models;
using JacRed.Models.AppConf;
using Newtonsoft.Json;

namespace JacRed.Configuration
{
    /// <summary>Strongly-typed mirror of init.yaml / init.conf root object.</summary>
    public class AppOptions
    {
        public string listenip = "any";

        public int listenport = 9117;

        public string apikey = null;

        /// <summary>Если задан — доступ к /dev/, /cron/, /jsondb и /api/v1.0/config/* из интернета и через туннель (заголовок X-Dev-Key или ?devkey=). В LAN без ключа не нужен.</summary>
        public string devkey = null;

        public bool mergeduplicates = true;

        public bool mergenumduplicates = true;

        public bool openstats = true;

        public bool opensync = true;

        public bool tracks = false;

        public bool web = true;

        /// <summary>
        /// 0 - все
        /// 1 - день, месяц
        /// </summary>
        public int tracksmod = 0;

        public int tracksdelay = 20_000;

        public bool trackslog = true;

        public int tracksatempt = 20;

        public string trackscategory = "jacred";

        public class TracksIntervalConfig
        {
            public int task0 { get; set; } = 180;
            public int task1 { get; set; } = 60;
        }

        [JsonProperty("tracksinterval")]
        public TracksIntervalConfig TracksInterval { get; set; } = new TracksIntervalConfig();

        public string[] tsuri = new string[] { "http://127.0.0.1:8090" };


        // When true, write FileDB add/update entries to Data/log/fdb.YYYY-MM-DD.log as JSON Lines (one JSON array per line; subject to retention/size/file limits).
        public bool logFdb = true;

        // Keep fdb log files only for this many days (0 = keep all). Applied when logFdb is true.
        public int logFdbRetentionDays = 7;

        // Max total size of fdb log files in MB (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxSizeMb = 0;

        // Max number of fdb log files to keep (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxFiles = 0;

        // When true, parsers write to Data/log/{tracker}.log for trackers that have log enabled in their settings.
        public bool logParsers = true;

        public string syncapi = null;

        public string[] synctrackers = null;

        public string[] disable_trackers = new string[] { };

        public bool syncsport = true;

        public bool syncspidr = true;

        public int maxreadfile = 200;

        public Evercache evercache = new Evercache() { enable = true, validHour = 1, maxOpenWriteTask = 2000, dropCacheTake = 200 };

        public int fdbPathLevels = 2;

        public int timeStatsUpdate = 90; // минут

        public int timeSync = 60; // минут

        public int timeSyncSpidr = 60; // минут (30, 60, 120 — без случайного смещения)

        /// <summary>During sync catch-up, flush masterDb + lastsync every N batches (0 = only time-based save every 5 min).</summary>
        public int saveCheckpointEveryNBatches = 5;

        public TrackerSettings Rutor = new TrackerSettings("http://rutor.info");

        public TrackerSettings Megapeer = new TrackerSettings("http://megapeer.vip");

        public TrackerSettings TorrentBy = new TrackerSettings("https://torrent.by");

        public TrackerSettings Kinozal = new TrackerSettings("https://kinozal.tv");

        public TrackerSettings NNMClub = new TrackerSettings("https://nnmclub.to");

        public TrackerSettings Bitru = new TrackerSettings("https://bitru.org");

        public TrackerSettings Toloka = new TrackerSettings("https://toloka.to");

        public TrackerSettings Mazepa = new TrackerSettings("https://mazepa.to");

        public TrackerSettings Rutracker = new TrackerSettings("https://rutracker.org");

        public TrackerSettings Selezen = new TrackerSettings("https://use.selezen.club");

        public TrackerSettings Lostfilm = new TrackerSettings("https://www.lostfilm.tv");

        public TrackerSettings Animelayer = new TrackerSettings("https://animelayer.ru");

        public TrackerSettings Anidub = new TrackerSettings("https://tr.anidub.com");

        public TrackerSettings Aniliberty = new TrackerSettings("https://aniliberty.top");

        public TrackerSettings Knaben = new TrackerSettings("https://api.knaben.org");

        // TODO: fix parser
        public TrackerSettings Baibako = new TrackerSettings("http://baibako.tv");

        public ProxySettings proxy = new ProxySettings();

        public SearchSettings search = new SearchSettings();

        public TorznabSettings torznab = new TorznabSettings();

        public LoggingOptions logging = new LoggingOptions
        {
            categories = new System.Collections.Generic.Dictionary<string, string>
            {
                ["parsers"] = "None"
            }
        };

        public System.Collections.Generic.List<ProxySettings> globalproxy;
    }
}

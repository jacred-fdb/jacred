using JacRed.Models;
using JacRed.Models.AppConf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed
{
    public class AppInit
    {
        #region AppInit
        static AppInit()
        {
            void updateConf()
            {
                try
                {
                    if (cacheconf.Item1 == null)
                    {
                        if (!File.Exists("init.conf"))
                        {
                            cacheconf.Item1 = new AppInit();
                            return;
                        }
                    }

                    var lastWriteTime = File.GetLastWriteTime("init.conf");

                    if (cacheconf.Item2 != lastWriteTime)
                    {
                        cacheconf.Item1 = JsonConvert.DeserializeObject<AppInit>(File.ReadAllText("init.conf"));
                        cacheconf.Item2 = lastWriteTime;
                    }
                }
                catch { }
            }

            updateConf();

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    updateConf();
                }
            });
        }

        static (AppInit, DateTime) cacheconf = default;

        public static AppInit conf => cacheconf.Item1;

        // Parser log is written only when parser log is enabled and this tracker's log is true.
        public static bool TrackerLogEnabled(string trackerName)
        {
            bool parserLogEnabled = conf?.logParsers == true || conf?.log == true;
            if (!parserLogEnabled || string.IsNullOrWhiteSpace(trackerName))
                return false;
            switch (trackerName.ToLowerInvariant())
            {
                case "baibako": return conf.Baibako.log;
                case "bitru": return conf.Bitru.log;
                case "kinozal": return conf.Kinozal.log;
                case "megapeer": return conf.Megapeer.log;
                case "nnmclub": return conf.NNMClub.log;
                case "rutor": return conf.Rutor.log;
                case "rutracker": return conf.Rutracker.log;
                case "selezen": return conf.Selezen.log;
                case "toloka": return conf.Toloka.log;
                case "torrentby": return conf.TorrentBy.log;
                case "lostfilm": return conf.Lostfilm.log;
                default: return false;
            }
        }
        #endregion


        public string listenip = "any";

        public int listenport = 9117;

        public string apikey = null;

        public bool mergeduplicates = true;

        public bool mergenumduplicates = true;

        public bool openstats = true;

        public bool opensync = true;

        public bool opensync_v1 = false;

        public bool tracks = false;

        public bool web = true;

        /// <summary>
        /// 0 - все
        /// 1 - день, месяц
        /// </summary>
        public int tracksmod = 0;

        public int tracksdelay = 20_000;

        public string[] tsuri = new string[] { "http://127.0.0.1:8090" };

        // Deprecated: use logFdb and logParsers. When true, enables both fdb and parser logs for backward compatibility.
        public bool log = false;

        // When true, write FileDB add/update entries to Data/log/fdb.YYYY-MM-DD.log as JSON Lines (one JSON array per line; subject to retention/size/file limits).
        public bool logFdb = false;

        // Keep fdb log files only for this many days (0 = keep all). Applied when logFdb is true.
        public int logFdbRetentionDays = 7;

        // Max total size of fdb log files in MB (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxSizeMb = 0;

        // Max number of fdb log files to keep (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxFiles = 0;

        // When true, parsers write to Data/log/{tracker}.log for trackers that have log enabled in their settings.
        public bool logParsers = false;

        public string syncapi = null;

        public string[] synctrackers = null;

        public string[] disable_trackers = new string[] { "hdrezka", "anifilm", "anilibria" };

        public bool syncsport = true;

        public bool syncspidr = true;

        public int maxreadfile = 200;

        public Evercache evercache = new Evercache() { enable = true, validHour = 1, maxOpenWriteTask = 2000, dropCacheTake = 200 };

        public int fdbPathLevels = 2;

        public int timeStatsUpdate = 90; // минут

        public int timeSync = 60; // минут

        public int timeSyncSpidr = 60; // минут (30, 60, 120 — без случайного смещения)

        public TrackerSettings Rutor = new TrackerSettings("http://rutor.info");

        public TrackerSettings Megapeer = new TrackerSettings("http://megapeer.vip");

        public TrackerSettings TorrentBy = new TrackerSettings("https://torrent.by");

        public TrackerSettings Kinozal = new TrackerSettings("https://kinozal.tv");

        public TrackerSettings NNMClub = new TrackerSettings("https://nnmclub.to");

        public TrackerSettings Bitru = new TrackerSettings("https://bitru.org");

        public TrackerSettings Toloka = new TrackerSettings("https://toloka.to");

        public TrackerSettings Rutracker = new TrackerSettings("https://rutracker.org");

        public TrackerSettings Selezen = new TrackerSettings("https://open.selezen.org");

        public TrackerSettings Anilibria = new TrackerSettings("https://api.anilibria.tv");

        public TrackerSettings Animelayer = new TrackerSettings("http://animelayer.ru");

        public TrackerSettings Anifilm = new TrackerSettings("https://anifilm.net");

        public TrackerSettings Rezka = new TrackerSettings("https://rezka.cc");

        public TrackerSettings Baibako = new TrackerSettings("http://baibako.tv");

        public TrackerSettings Lostfilm = new TrackerSettings("https://www.lostfilm.tv");


        public ProxySettings proxy = new ProxySettings();

        public List<ProxySettings> globalproxy = new List<ProxySettings>()
        {
            new ProxySettings()
            {
                pattern = "\\.onion",
                list = new List<string>() { "socks5://127.0.0.1:9050" }
            }
        };
    }
}

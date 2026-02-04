using JacRed.Models;
using JacRed.Models.AppConf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace JacRed
{
    public class AppInit
    {
        private static readonly HashSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apikey", "cookie", "u", "p", "username", "password"
        };

        /// <summary>
        /// Returns current configuration as JSON with sensitive values (apikey, cookie, login, proxy auth) redacted.
        /// </summary>
        public static string GetSafeConfigJson()
        {
            var c = conf;
            if (c == null) return "{}";
            var jo = JObject.FromObject(c);
            RedactSensitive(jo);
            return jo.ToString(Formatting.Indented);
        }

        private static void RedactSensitive(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties().ToList())
                {
                    if (SensitiveKeys.Contains(prop.Name) && prop.Value != null && prop.Value.Type != JTokenType.Null && prop.Value.Type != JTokenType.Undefined)
                    {
                        var val = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val))
                            prop.Value = "***";
                    }
                    else
                        RedactSensitive(prop.Value);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                    RedactSensitive(item);
            }
        }

        private static void LogSafeConfig(string label, string source = null)
        {
            try
            {
                var src = string.IsNullOrEmpty(source) ? "" : $" from {source}";
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {label}{src} applied (sensitive data redacted):");
                Console.WriteLine(GetSafeConfigJson());
            }
            catch { }
        }

        private const string ConfigFileYaml = "init.yaml";
        private const string ConfigFileJson = "init.conf";

        /// <summary>
        /// Config file priority: init.yaml wins over init.conf. If both exist, init.yaml is used.
        /// </summary>
        private static (string path, DateTime lastWrite) GetConfigSource()
        {
            var hasYaml = File.Exists(ConfigFileYaml);
            var hasJson = File.Exists(ConfigFileJson);
            if (hasYaml)
                return (ConfigFileYaml, File.GetLastWriteTimeUtc(ConfigFileYaml));
            if (hasJson)
                return (ConfigFileJson, File.GetLastWriteTimeUtc(ConfigFileJson));
            return (null, default);
        }

        private static AppInit LoadConfigFromFile(string path)
        {
            var text = File.ReadAllText(path);
            if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                var deserializer = new DeserializerBuilder().Build();
                var yamlObj = deserializer.Deserialize<object>(new StringReader(text));
                var json = JsonConvert.SerializeObject(yamlObj);
                return JsonConvert.DeserializeObject<AppInit>(json);
            }
            return JsonConvert.DeserializeObject<AppInit>(text);
        }

        #region AppInit
        static AppInit()
        {
            void updateConf()
            {
                try
                {
                    var (path, lastWrite) = GetConfigSource();

                    if (cacheconf.Item1 == null)
                    {
                        if (path == null)
                        {
                            cacheconf.Item1 = new AppInit();
                            cacheconf.Item2 = null;
                            cacheconf.Item3 = default;
                            LogSafeConfig("config (default)");
                            return;
                        }
                        cacheconf.Item1 = LoadConfigFromFile(path);
                        cacheconf.Item2 = path;
                        cacheconf.Item3 = lastWrite;
                        LogSafeConfig("config (start)", path);
                        return;
                    }

                    if (path == null)
                        return;

                    if (cacheconf.Item2 != path || cacheconf.Item3 != lastWrite)
                    {
                        bool isReload = cacheconf.Item2 != null;
                        cacheconf.Item1 = LoadConfigFromFile(path);
                        cacheconf.Item2 = path;
                        cacheconf.Item3 = lastWrite;
                        LogSafeConfig(isReload ? "config (reload)" : "config (start)", path);
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

        static (AppInit, string path, DateTime lastWrite) cacheconf = default;

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
                case "mazepa": return conf.Mazepa.log;
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

        public TrackerSettings Mazepa = new TrackerSettings("https://mazepa.to");

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

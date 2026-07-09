using JacRed.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Configuration
{
    public static class ConfigSchema
    {
        public static readonly HashSet<string> KnownTrackerSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rutracker", "rutor", "kinozal", "nnmclub", "megapeer", "bitru", "toloka", "mazepa",
            "lostfilm", "baibako", "torrentby", "selezen", "animelayer", "anidub", "aniliberty", "knaben"
        };

        public static readonly HashSet<string> TrackerBlockNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rutor", "Megapeer", "TorrentBy", "Kinozal", "NNMClub", "Bitru", "Toloka", "Mazepa",
            "Rutracker", "Selezen", "Lostfilm", "Animelayer", "Anidub", "Aniliberty", "Knaben", "Baibako"
        };

        public static readonly HashSet<string> SensitiveFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apikey", "devkey", "cookie", "u", "p", "username", "password"
        };

        public static object Get()
        {
            return new
            {
                groups = new object[]
                {
                    Group("server", "Сервер", "Прослушивание и ключи доступа", new[]
                    {
                        Field("listenip", "string", "IP прослушивания", "any или конкретный IP"),
                        Field("listenport", "int", "Порт", "1–65535", min: 1, max: 65535),
                        Field("apikey", "password", "API ключ", "Пусто — без проверки", sensitive: true),
                        Field("devkey", "password", "Dev ключ", "Для /settings, /dev/, /cron/, /jsondb из интернета и через туннель", sensitive: true),
                        Field("web", "bool", "Веб-интерфейс", "Раздавать статику (PWA)")
                    }),
                    Group("api", "API и дубликаты", null, new[]
                    {
                        Field("openstats", "bool", "Открытая статистика", null),
                        Field("opensync", "bool", "Открытый sync", null),
                        Field("opensync_v1", "bool", "Sync v1", "Старый формат sync"),
                        Field("mergeduplicates", "bool", "Объединять дубликаты", null),
                        Field("mergenumduplicates", "bool", "Объединять по номеру", "Серии и т.п.")
                    }),
                    Group("sync", "Синхронизация", null, new[]
                    {
                        Field("syncapi", "string", "Sync API URL", "URL удалённого JacRed"),
                        Field("synctrackers", "stringList", "Sync трекеры", "Трекеры для синхронизации с удалённым JacRed", enumValues: KnownTrackerSlugs.OrderBy(x => x).ToArray()),
                        Field("disable_trackers", "stringList", "Отключённые трекеры", "Трекеры, которые не должны работать на этом инстансе", enumValues: KnownTrackerSlugs.OrderBy(x => x).ToArray()),
                        Field("syncsport", "bool", "Sync sport", null),
                        Field("syncspidr", "bool", "Sync spidr", null),
                        Field("timeSync", "int", "Интервал sync (мин)", null, min: 1),
                        Field("timeSyncSpidr", "int", "Интервал sync spidr (мин)", null, min: 1),
                        Field("maxreadfile", "int", "Max read file", "Лимит чтения fdb", min: 1)
                    }),
                    Group("logging", "Логирование", null, new[]
                    {
                        Field("log", "bool", "Лог (legacy)", "Включает logFdb и logParsers"),
                        Field("logFdb", "bool", "Лог FileDB", null),
                        Field("logFdbRetentionDays", "int", "Хранение fdb логов (дней)", "0 — все", min: 0),
                        Field("logFdbMaxSizeMb", "int", "Max размер fdb логов (MB)", "0 — без лимита", min: 0),
                        Field("logFdbMaxFiles", "int", "Max файлов fdb логов", "0 — без лимита", min: 0),
                        Field("logParsers", "bool", "Лог парсеров", null)
                    }),
                    Group("tracks", "Tracks (ffprobe)", null, new[]
                    {
                        Field("tracks", "bool", "Включить tracks", "Сбор метаданных через tsuri"),
                        Field("trackslog", "bool", "Лог tracks", null),
                        Field("trackscategory", "string", "Категория tracks", "Уникально для инстанса"),
                        Field("tracksdelay", "int", "Задержка tsuri (мс)", null, min: 0),
                        Field("tracksatempt", "int", "Попыток tracks", null, min: 1),
                        Field("tracksmod", "select", "Режим tracks", "0 — все, 1 — за сутки", enumValues: new[] { "0", "1" }),
                        Field("tracksinterval.task0", "int", "Tracks task0 (мин)", "Все задачи", min: 1),
                        Field("tracksinterval.task1", "int", "Tracks task1 (мин)", "За сутки", min: 1),
                        Field("tsuri", "stringList", "TSURI", "URL сервисов ffprobe, по одному на строку")
                    }),
                    Group("fdb", "FileDB", null, new[]
                    {
                        Field("fdbPathLevels", "int", "Уровни fdb", "1–4", min: 1, max: 4),
                        Field("timeStatsUpdate", "int", "Обновление stats (мин)", null, min: 1)
                    }),
                    Group("evercache", "Evercache", null, new[]
                    {
                        Field("evercache.enable", "bool", "Включить", null),
                        Field("evercache.validHour", "int", "Valid hour", "0 — бессрочно", min: 0),
                        Field("evercache.maxOpenWriteTask", "int", "Max open write", null, min: 1),
                        Field("evercache.dropCacheTake", "int", "Drop cache take", null, min: 1)
                    }),
                    Group("search", "Поиск (combined)", "Jackett JSON + Torznab combined search", new[]
                    {
                        Field("search.mergeV1", "select", "Merge v1 (fuzzy)", "auto — только fuzzy; card без v1", enumValues: new[] { "false", "auto", "true" }),
                        Field("search.maxV1Pairs", "int", "Max v1 pairs", "При mergeV1=auto или true (fuzzy)", min: 1),
                        Field("search.v1Sort", "string", "V1 sort", "sid, pir, size…"),
                        Field("search.stripTrailingYear", "bool", "Strip trailing year", "Fuzzy: запрос без года"),
                        Field("search.skipCatFilter", "bool", "Skip cat filter", "Не фильтровать cat/Category[] на сервере")
                    }),
                    Group("torznab", "Torznab", "Torznab XML (Sonarr/Radarr/Prowlarr)", new[]
                    {
                        Field("torznab.enable", "bool", "Torznab XML", "/torznab/api и Torznab-алиасы"),
                        Field("torznab.enrichTitles", "bool", "Enrich titles", "Озвучки в XML title")
                    }),
                    Group("proxy", "Прокси", null, new[]
                    {
                        Field("proxy.pattern", "string", "Pattern", "Regex для proxy"),
                        Field("proxy.useAuth", "bool", "Use auth", null),
                        Field("proxy.BypassOnLocal", "bool", "Bypass on local", null),
                        Field("proxy.username", "password", "Username", null, sensitive: true),
                        Field("proxy.password", "password", "Password", null, sensitive: true),
                        Field("proxy.list", "stringList", "Proxy list", "ip:port или socks5://…"),
                        Field("globalproxy", "json", "Global proxy", "JSON-массив ProxySettings")
                    }),
                    TrackerGroups()
                }
            };
        }

        private static object TrackerGroups()
        {
            var trackers = TrackerBlockNames.OrderBy(x => x).Select(name => new
            {
                id = name,
                title = name,
                description = (string)null,
                trackerSlug = name.ToLowerInvariant(),
                fields = new object[]
                {
                    Field("alias", "string", "Alias URL", "Onion/worker URL"),
                    Field("useproxy", "bool", "Use proxy", null),
                    Field("reqMinute", "int", "Запросов/мин", "-1 — отключить", min: -1),
                    Field("log", "bool", "Лог парсера", null),
                    Field("cookie", "password", "Cookie", "Статический cookie", sensitive: true),
                    Field("login.u", "password", "Login", null, sensitive: true),
                    Field("login.p", "password", "Password", null, sensitive: true)
                }
            }).ToArray();

            return new
            {
                id = "trackers",
                title = "Трекеры",
                description = "Настройки парсеров (host задаётся по умолчанию)",
                trackers
            };
        }

        private static object Group(string id, string title, string description, object[] fields) => new
        {
            id,
            title,
            description,
            fields
        };

        private static object Field(string key, string type, string label, string description = null,
            bool sensitive = false, int? min = null, int? max = null, string[] enumValues = null) => new
            {
                key,
                type,
                label,
                description,
                sensitive,
                min,
                max,
                enumValues
            };

        public static void ValidateAgainstSchema(AppOptions config, List<string> errors, List<string> warnings)
        {
            if (config == null)
            {
                errors.Add("Конфигурация не задана");
                return;
            }

            if (config.listenport < 1 || config.listenport > 65535)
                errors.Add("listenport: значение должно быть от 1 до 65535");

            if (!string.IsNullOrEmpty(config.listenip) &&
                !string.Equals(config.listenip, "any", StringComparison.OrdinalIgnoreCase) &&
                !System.Net.IPAddress.TryParse(config.listenip, out _))
                warnings.Add("listenip: ожидается 'any' или корректный IP-адрес");

            if (config.tracksmod != 0 && config.tracksmod != 1)
                errors.Add("tracksmod: допустимы только 0 или 1");

            if (config.timeSync < 1)
                errors.Add("timeSync: должно быть ≥ 1");

            if (config.timeStatsUpdate < 1)
                errors.Add("timeStatsUpdate: должно быть ≥ 1");

            if (config.maxreadfile < 1)
                errors.Add("maxreadfile: должно быть ≥ 1");

            if (config.fdbPathLevels < 1 || config.fdbPathLevels > 4)
                warnings.Add("fdbPathLevels: рекомендуется 1–4");

            if (config.logFdbRetentionDays < 0)
                errors.Add("logFdbRetentionDays: не может быть отрицательным");

            if (config.tracksdelay < 0)
                errors.Add("tracksdelay: не может быть отрицательным");

            if (config.tracksatempt < 1)
                errors.Add("tracksatempt: должно быть ≥ 1");

            ValidateTrackerList(config.synctrackers, "synctrackers", warnings);
            ValidateTrackerList(config.disable_trackers, "disable_trackers", warnings);

            if (config.tsuri != null)
            {
                foreach (var uri in config.tsuri)
                {
                    if (string.IsNullOrWhiteSpace(uri)) continue;
                    if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) ||
                        (u.Scheme != "http" && u.Scheme != "https"))
                        warnings.Add($"tsuri: некорректный URL «{uri}»");
                }
            }

            ValidateMergeV1(config.search?.mergeV1, "search.mergeV1", errors);

            if (config.evercache != null)
            {
                if (config.evercache.validHour < 0)
                    errors.Add("evercache.validHour: не может быть отрицательным");
                if (config.evercache.maxOpenWriteTask < 1)
                    errors.Add("evercache.maxOpenWriteTask: должно быть ≥ 1");
            }

            if (config.TracksInterval != null)
            {
                if (config.TracksInterval.task0 < 1)
                    errors.Add("tracksinterval.task0: должно быть ≥ 1");
                if (config.TracksInterval.task1 < 1)
                    errors.Add("tracksinterval.task1: должно быть ≥ 1");
            }
        }

        private static void ValidateMergeV1(string value, string fieldName, List<string> errors)
        {
            if (value == null) return;
            var mv = value.ToLowerInvariant();
            if (mv != "auto" && mv != "true" && mv != "false")
                errors.Add($"{fieldName}: допустимы auto, true, false");
        }

        private static void ValidateTrackerList(string[] list, string fieldName, List<string> warnings)
        {
            if (list == null) return;
            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (!KnownTrackerSlugs.Contains(item))
                    warnings.Add($"{fieldName}: неизвестный трекер «{item}»");
            }
        }

        public static List<ConfigDiffEntry> ComputeDiff(JToken current, JToken proposed, string pathPrefix = "")
        {
            var diffs = new List<ConfigDiffEntry>();

            if (current == null && proposed == null) return diffs;
            if (current == null && proposed != null)
            {
                diffs.Add(new ConfigDiffEntry { path = pathPrefix.TrimEnd('.'), oldValue = null, newValue = FormatToken(proposed), change = "added" });
                return diffs;
            }
            if (proposed == null && current != null)
            {
                diffs.Add(new ConfigDiffEntry { path = pathPrefix.TrimEnd('.'), oldValue = FormatToken(current), newValue = null, change = "removed" });
                return diffs;
            }

            if (current is JObject curObj && proposed is JObject propObj)
            {
                var allKeys = curObj.Properties().Select(p => p.Name)
                    .Union(propObj.Properties().Select(p => p.Name), StringComparer.Ordinal)
                    .ToList();

                foreach (var key in allKeys)
                {
                    var childPath = string.IsNullOrEmpty(pathPrefix) ? key : pathPrefix + key;
                    curObj.TryGetValue(key, out var curVal);
                    propObj.TryGetValue(key, out var propVal);

                    if (curVal == null && propVal == null) continue;
                    if (curVal == null || propVal == null || curVal.Type != propVal.Type ||
                        (curVal is JValue && propVal is JValue))
                    {
                        if (!TokenValueEquals(curVal, propVal))
                        {
                            diffs.Add(new ConfigDiffEntry
                            {
                                path = childPath,
                                oldValue = FormatToken(curVal),
                                newValue = FormatToken(propVal),
                                sensitive = IsSensitivePath(childPath),
                                change = curVal == null ? "added" : propVal == null ? "removed" : "changed"
                            });
                        }
                        continue;
                    }

                    if (curVal is JObject || curVal is JArray)
                        diffs.AddRange(ComputeDiff(curVal, propVal, childPath + "."));
                }
                return diffs;
            }

            if (current is JArray curArr && proposed is JArray propArr)
            {
                if (!ArrayValueEquals(pathPrefix.TrimEnd('.'), curArr, propArr))
                {
                    diffs.Add(new ConfigDiffEntry
                    {
                        path = pathPrefix.TrimEnd('.'),
                        oldValue = FormatToken(curArr),
                        newValue = FormatToken(propArr),
                        sensitive = IsSensitivePath(pathPrefix),
                        change = "changed"
                    });
                }
                return diffs;
            }

            if (!TokenValueEquals(current, proposed))
            {
                diffs.Add(new ConfigDiffEntry
                {
                    path = pathPrefix.TrimEnd('.'),
                    oldValue = FormatToken(current),
                    newValue = FormatToken(proposed),
                    sensitive = IsSensitivePath(pathPrefix),
                    change = "changed"
                });
            }

            return diffs;
        }

        private static JToken NormalizeToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return JValue.CreateNull();
            return token;
        }

        private static bool TokenValueEquals(JToken a, JToken b)
        {
            if (JToken.DeepEquals(NormalizeToken(a), NormalizeToken(b)))
                return true;

            if (IsEmptyScalar(a) && IsEmptyScalar(b))
                return true;

            if (a is JValue va && b is JValue vb)
            {
                if (va.Type == JTokenType.Integer && vb.Type == JTokenType.String &&
                    long.TryParse(vb.ToString(), out var n) && va.Value<long>() == n)
                    return true;
                if (vb.Type == JTokenType.Integer && va.Type == JTokenType.String &&
                    long.TryParse(va.ToString(), out n) && vb.Value<long>() == n)
                    return true;
            }

            return false;
        }

        private static bool IsEmptyScalar(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return true;
            if (token.Type == JTokenType.String && string.IsNullOrEmpty(token.ToString())) return true;
            if (token is JArray arr && arr.Count == 0) return true;
            return false;
        }

        private static bool ArrayValueEquals(string path, JArray a, JArray b)
        {
            if (JToken.DeepEquals(NormalizeToken(a), NormalizeToken(b)))
                return true;

            if (IsTrackerSlugList(path))
            {
                var sa = a.Select(x => x?.ToString() ?? "").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                var sb = b.Select(x => x?.ToString() ?? "").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                return sa.SequenceEqual(sb, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool IsTrackerSlugList(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var last = path.Split('.').LastOrDefault() ?? path;
            return string.Equals(last, "synctrackers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(last, "disable_trackers", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "—";
            if (token is JValue v)
                return v.ToString();
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool IsSensitivePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var last = path.Split('.').LastOrDefault() ?? path;
            return SensitiveFieldNames.Contains(last);
        }
    }

    public class ConfigDiffEntry
    {
        public string path { get; set; }
        public string oldValue { get; set; }
        public string newValue { get; set; }
        public bool sensitive { get; set; }
        public string change { get; set; }
    }
}

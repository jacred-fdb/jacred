using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using JacRed.Infrastructure.Trackers;
using JacRed.Infrastructure.Tracks;
using JacRed.Models.Api;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;

namespace JacRed.Application.Search
{
    internal static class JackettResultBuilder
    {
        #region AddTorrents
        public static void AddTorrent(Dictionary<string, TorrentDetails> torrents, TorrentDetails t)
        {
            if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName))
                return;

            if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName))
                return;

            if (torrents.TryGetValue(t.url, out TorrentDetails val))
            {
                if (t.updateTime > val.updateTime)
                    torrents[t.url] = t;
            }
            else
            {
                torrents.TryAdd(t.url, t);
            }
        }
        #endregion

        #region getCategoryIds
        static HashSet<int> GetCategoryIds(TorrentDetails t, out string categoryDesc)
        {
            categoryDesc = null;
            HashSet<int> categoryIds = new HashSet<int>(t.types.Length);

            foreach (string type in t.types)
            {
                switch (type)
                {
                    case "movie":
                        categoryDesc = "Movies";
                        categoryIds.Add(2000);
                        break;

                    case "serial":
                        categoryDesc = "TV";
                        categoryIds.Add(5000);
                        break;

                    case "documovie":
                    case "docuserial":
                        categoryDesc = "TV/Documentary";
                        categoryIds.Add(5080);
                        break;

                    case "tvshow":
                        categoryDesc = "TV/Foreign";
                        categoryIds.Add(5020);
                        categoryIds.Add(2010);
                        break;

                    case "anime":
                        categoryDesc = "TV/Anime";
                        categoryIds.Add(5070);
                        break;
                }
            }

            return categoryIds;
        }
        #endregion

        #region Объединить дубликаты
        static IEnumerable<TorrentDetails> MergeDuplicates(Dictionary<string, TorrentDetails> torrents, bool rqnum)
        {
            IEnumerable<TorrentDetails> result;

            if ((!rqnum && AppInit.conf.mergeduplicates) || (rqnum && AppInit.conf.mergenumduplicates))
            {
                Dictionary<string, (TorrentDetails torrent, string title, string Name, List<string> AnnounceUrls)> temp = new Dictionary<string, (TorrentDetails, string, string, List<string>)>();
                // The copy the merged record (title, link, size, info) comes from, by hash.
                var records = new Dictionary<string, TorrentDetails>();

                foreach (var torrent in torrents.Values.OrderByDescending(i => i.createTime).ThenBy(i => i.trackerName == "selezen"))
                {
                    MagnetLink magnetLink;
                    string hex;
                    try
                    {
                        magnetLink = MagnetLink.Parse(torrent.magnet);
                        hex = magnetLink.InfoHashes.V1OrV2.ToHex();
                    }
                    catch
                    {
                        // Malformed magnets must not break the whole merge batch.
                        continue;
                    }

                    if (!temp.TryGetValue(hex, out _))
                    {
                        temp.TryAdd(hex, ((TorrentDetails)torrent.Clone(), torrent.trackerName == "kinozal" ? torrent.title : null, magnetLink.Name, magnetLink.AnnounceUrls?.ToList() ?? new List<string>()));
                        records[hex] = torrent;
                    }
                    else
                    {
                        var t = temp[hex];
                        bool reliableSeeders = UnreliableSeeders.AnyReliable(t.torrent.trackerName);

                        // A renamed topic keeps its old record in the old bucket (and under an old domain):
                        // a later copy on the same tracker has the current title.
                        var record = records[hex];
                        if (torrent.trackerName == record.trackerName && torrent.updateTime > record.updateTime)
                        {
                            records[hex] = torrent;
                            TakeRecord(t.torrent, torrent, keepTitle: t.title != null);
                        }

                        if (!t.torrent.trackerName.Contains(torrent.trackerName))
                            t.torrent.trackerName += $", {torrent.trackerName}";

                        #region UpdateMagnet
                        void UpdateMagnet()
                        {
                            var magnet = new StringBuilder($"magnet:?xt=urn:btih:{hex.ToLower()}");

                            if (!string.IsNullOrWhiteSpace(t.Name))
                                magnet.Append($"&dn={HttpUtility.UrlEncode(t.Name)}");

                            if (t.AnnounceUrls.Count > 0)
                            {
                                var addedTr = new HashSet<string>();
                                foreach (string announce in t.AnnounceUrls)
                                {
                                    string tr = announce.Contains("/") || announce.Contains(":") ? HttpUtility.UrlEncode(announce) : announce;

                                    if (addedTr.Add(tr))
                                        magnet.Append($"&tr={tr}");
                                }
                            }

                            t.torrent.magnet = magnet.ToString();
                        }
                        #endregion

                        if (string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(magnetLink.Name))
                        {
                            t.Name = magnetLink.Name;
                            temp[hex] = t;
                            UpdateMagnet();
                        }

                        if (magnetLink.AnnounceUrls != null && magnetLink.AnnounceUrls.Count > 0)
                        {
                            t.AnnounceUrls.AddRange(magnetLink.AnnounceUrls);
                            UpdateMagnet();
                        }

                        #region UpdateTitle
                        void UpdateTitle()
                        {
                            if (string.IsNullOrWhiteSpace(t.title))
                                return;

                            string title = t.title;

                            if (t.torrent.voices != null && t.torrent.voices.Count > 0)
                                title += $" | {string.Join(" | ", t.torrent.voices)}";

                            t.torrent.title = title;
                        }

                        if (torrent.trackerName == "kinozal")
                        {
                            t.title = torrent.title;
                            temp[hex] = t;
                            UpdateTitle();
                        }

                        if (torrent.voices != null && torrent.voices.Count > 0)
                        {
                            if (t.torrent.voices == null)
                            {
                                t.torrent.voices = new HashSet<string>(torrent.voices);
                            }
                            else
                            {
                                foreach (var v in torrent.voices)
                                    t.torrent.voices.Add(v);
                            }

                            UpdateTitle();
                        }
                        #endregion

                        if (UnreliableSeeders.AnyReliable(torrent.trackerName))
                        {
                            if (!reliableSeeders)
                            {
                                t.torrent.sid = torrent.sid;
                                t.torrent.pir = torrent.pir;
                            }

                            if (torrent.sid > t.torrent.sid)
                                t.torrent.sid = torrent.sid;

                            if (torrent.pir > t.torrent.pir)
                                t.torrent.pir = torrent.pir;
                        }

                        if (torrent.createTime > t.torrent.createTime)
                            t.torrent.createTime = torrent.createTime;

                        if (torrent.voices != null && torrent.voices.Count > 0)
                        {
                            if (t.torrent.voices == null)
                                t.torrent.voices = new HashSet<string>();

                            foreach (var v in torrent.voices)
                                t.torrent.voices.Add(v);
                        }

                        if (torrent.languages != null && torrent.languages.Count > 0)
                        {
                            if (t.torrent.languages == null)
                                t.torrent.languages = new HashSet<string>();

                            foreach (var v in torrent.languages)
                                t.torrent.languages.Add(v);
                        }

                        if (t.torrent.ffprobe == null && torrent.ffprobe != null)
                            t.torrent.ffprobe = torrent.ffprobe;
                    }
                }

                result = temp.Select(i => i.Value.torrent);
            }
            else
            {
                result = torrents.Values;
            }

            return result;
        }
        #endregion

        /// <summary>The record fields of <paramref name="from"/>; trackers, magnet, seeders, voices and tracks stay merged.</summary>
        static void TakeRecord(TorrentDetails into, TorrentDetails from, bool keepTitle)
        {
            into.url = from.url;
            if (!keepTitle)
                into.title = from.title;
            into.size = from.size;
            into.sizeName = from.sizeName;
            into.types = from.types;
            into.name = from.name;
            into.originalname = from.originalname;
            into._sn = from._sn;
            into._so = from._so;
            into.relased = from.relased;
            into.quality = from.quality;
            into.videotype = from.videotype;
            into.seasons = from.seasons;
            into.updateTime = from.updateTime;
        }

        #region FFprobe
        static List<ffStream> GetFfprobe(TorrentDetails t, out HashSet<string> langs)
        {
            langs = t.languages;

            if (t.ffprobe != null || !AppInit.conf.tracks)
            {
                langs = TracksDB.Languages(t, t.ffprobe);
                return t.ffprobe;
            }

            var streams = TracksDB.Get(t.magnet, t.types);
            langs = TracksDB.Languages(t, streams ?? t.ffprobe);
            if (streams == null)
                return null;

            return streams;
        }
        #endregion

        public static List<Result> Build(Dictionary<string, TorrentDetails> torrents, string apikey, bool rqnum)
        {
            var result = MergeDuplicates(torrents, rqnum);

            if (apikey == "rus")
                result = result.Where(i => (i.languages != null && i.languages.Contains("rus")) || (i.types != null && (i.types.Contains("sport") || i.types.Contains("tvshow") || i.types.Contains("docuserial"))));

            var Results = new List<Result>(torrents.Count);

            foreach (var i in result)
            {
                HashSet<string> languages = null;
                var ffprobe = rqnum ? null : GetFfprobe(i, out languages);

                Results.Add(new Result()
                {
                    Tracker = i.trackerName,
                    Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
                    Title = i.title,
                    Size = i.size,
                    PublishDate = i.createTime,
                    UpdateTime = i.updateTime,
                    Category = GetCategoryIds(i, out string categoryDesc),
                    CategoryDesc = categoryDesc,
                    Seeders = i.sid,
                    Peers = i.pir,
                    MagnetUri = i.magnet,
                    ffprobe = ffprobe,
                    languages = languages,
                    info = rqnum ? null : new TorrentInfo()
                    {
                        name = i.name,
                        originalname = i.originalname,
                        sizeName = i.sizeName,
                        relased = i.relased,
                        videotype = i.videotype,
                        quality = i.quality,
                        voices = i.voices,
                        seasons = i.seasons != null && i.seasons.Count > 0 ? i.seasons : null,
                        types = i.types
                    }
                });
            }

            return Results;
        }
    }
}

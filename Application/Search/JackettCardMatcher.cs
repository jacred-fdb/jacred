using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Application.Search
{
    internal static class JackettCardMatcher
    {
        public static Dictionary<string, TorrentDetails> Search(
            IFastDbIndex fastDbIndex,
            string query,
            string title,
            string title_original,
            int year,
            Dictionary<string, string> category,
            int is_serial,
            bool rqnum,
            IMemoryCache memoryCache)
        {
            var fastdb = fastDbIndex.Get();
            var torrents = new Dictionary<string, TorrentDetails>();

            #region Запрос с NUM
            if (rqnum && query != null)
            {
                var mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

                if (mNum.Success)
                {
                    if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                    {
                        var g = mNum.Groups;
                        title = g[1].Value;
                        title_original = g[2].Value;
                        year = int.Parse(g[3].Value);
                    }
                }
                else
                {
                    if (Regex.IsMatch(query, "^([^a-z-A-Z]+) ((19|20)[0-9]{2})$"))
                        return torrents;

                    mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+)$");

                    if (mNum.Success)
                    {
                        if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                        {
                            var g = mNum.Groups;
                            title = g[1].Value;
                            title_original = g[2].Value;
                        }
                    }
                }
            }
            #endregion

            #region category
            if (is_serial == 0 && category != null)
            {
                string cat = category.FirstOrDefault().Value;
                if (cat != null)
                {
                    if (cat.Contains("5020") || cat.Contains("2010"))
                        is_serial = 3; // tvshow
                    else if (cat.Contains("5080"))
                        is_serial = 4; // док
                    else if (cat.Contains("5070"))
                        is_serial = 5; // аниме
                    else
                    {
                        if (cat.StartsWith("20"))
                            is_serial = 1; // фильм
                        else if (cat.StartsWith("50"))
                            is_serial = 2; // сериал
                    }
                }
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(title_original))
            {
                #region Точный поиск
                string _n = StringConvert.SearchName(title);
                string _o = StringConvert.SearchName(title_original);

                HashSet<string> keys = new HashSet<string>(20);

                void updateKeys(string k)
                {
                    if (k != null && fastdb.TryGetValue(k, out List<string> _keys))
                    {
                        foreach (string val in _keys)
                            keys.Add(val);
                    }
                }

                updateKeys(_n);
                updateKeys(_o);

                if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0) && keys.Count > AppInit.conf.maxreadfile)
                    keys = keys.Take(AppInit.conf.maxreadfile).ToHashSet();

                foreach (string key in keys)
                {
                    foreach (var t in FileDB.OpenRead(key, true).Values)
                    {
                        if (t.types == null || t.title.Contains(" КПК"))
                            continue;

                        string name = t._sn ?? StringConvert.SearchName(t.name);
                        string originalname = t._so ?? StringConvert.SearchName(t.originalname);

                        // Точная выборка по name или originalname
                        if ((_n != null && _n == name) || (_o != null && _o == originalname))
                        {
                            if (is_serial == 1)
                            {
                                #region Фильм
                                if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                {
                                    if (Regex.IsMatch(t.title, " (сезон|сери(и|я|й))", RegexOptions.IgnoreCase))
                                        continue;

                                    if (year > 0)
                                    {
                                        if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 2)
                            {
                                #region Сериал
                                if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 3)
                            {
                                #region tvshow
                                if (t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 4)
                            {
                                #region docuserial / documovie
                                if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 5)
                            {
                                #region anime
                                if (t.types.Contains("anime"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                #endregion
                            }
                            else
                            {
                                #region Неизвестно
                                if (year > 0)
                                {
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("documovie"))
                                    {
                                        if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                    else
                                    {
                                        if (t.relased >= (year - 1))
                                            JackettResultBuilder.AddTorrent(torrents, t);
                                    }
                                }
                                else
                                {
                                    JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                #endregion
                            }
                        }
                    }

                }
                #endregion
            }
            else if (!string.IsNullOrWhiteSpace(query) && query.Length > 1)
            {
                #region Обычный поиск
                string _s = StringConvert.SearchName(query);

                #region torrentsSearch
                void torrentsSearch(bool exact, bool exactdb)
                {
                    if (_s == null)
                        return;

                    HashSet<string> keys = null;

                    if (exactdb)
                    {
                        if (fastdb.TryGetValue(_s, out List<string> _keys) && _keys.Count > 0)
                        {
                            keys = new HashSet<string>(_keys.Count);

                            foreach (string val in _keys)
                                keys.Add(val);
                        }
                    }
                    else
                    {
                        string mkey = $"api:torrentsSearch:{_s}";
                        if (!memoryCache.TryGetValue(mkey, out keys))
                        {
                            keys = new HashSet<string>();

                            foreach (var f in fastdb.Where(i => i.Key.Contains(_s)))
                            {
                                foreach (string k in f.Value)
                                    keys.Add(k);

                                if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0) && keys.Count > AppInit.conf.maxreadfile)
                                    break;
                            }

                            memoryCache.Set(mkey, keys, DateTime.Now.AddHours(1));
                        }
                    }

                    if (keys != null && keys.Count > 0)
                    {
                        foreach (string key in keys)
                        {
                            foreach (var t in FileDB.OpenRead(key, true).Values)
                            {
                                if (exact)
                                {
                                    if ((t._sn ?? StringConvert.SearchName(t.name)) != _s && (t._so ?? StringConvert.SearchName(t.originalname)) != _s)
                                        continue;
                                }

                                if (t.types == null || t.title.Contains(" КПК"))
                                    continue;

                                if (is_serial == 1)
                                {
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                else if (is_serial == 2)
                                {
                                    if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                else if (is_serial == 3)
                                {
                                    if (t.types.Contains("tvshow"))
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                else if (is_serial == 4)
                                {
                                    if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                else if (is_serial == 5)
                                {
                                    if (t.types.Contains("anime"))
                                        JackettResultBuilder.AddTorrent(torrents, t);
                                }
                                else
                                {
                                    JackettResultBuilder.AddTorrent(torrents, t);
                                }
                            }

                        }
                    }
                }
                #endregion

                if (is_serial == -1)
                {
                    torrentsSearch(exact: false, exactdb: true);
                    if (torrents.Count == 0)
                        torrentsSearch(exact: false, exactdb: false);
                }
                else
                {
                    torrentsSearch(exact: true, exactdb: true);
                    if (torrents.Count == 0)
                        torrentsSearch(exact: false, exactdb: false);
                }
                #endregion
            }


            return torrents;
        }
    }
}

using BencodeNET.Parsing;
using BencodeNET.Torrents;
using System.Text.RegularExpressions;

namespace JacRed.Engine.Parsing
{
    public static class BencodeTo
    {
        #region Magnet
        public static string Magnet(byte[] torrent)
        {
            try
            {
                if (torrent == null)
                    return null;

                var parser = new BencodeParser();
                var res = parser.Parse<Torrent>(torrent);

                string magnet = res.GetMagnetLink();
                if (res.OriginalInfoHash != null)
                    magnet = Regex.Replace(magnet, @"urn:btih:[\w0-9]+", $"urn:btih:{res.OriginalInfoHash.ToLower()}", RegexOptions.IgnoreCase);

                return magnet;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region SizeName
        public static string SizeName(byte[] torrent)
        {
            try
            {
                if (torrent == null)
                    return null;

                var parser = new BencodeParser();
                var res = parser.Parse<Torrent>(torrent);

                string FormatBytes(long bytes)
                {
                    string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
                    int i;
                    double dblSByte = bytes;
                    for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
                    {
                        dblSByte = bytes / 1024.0;
                    }

                    return string.Format("{0:N2} {1}", dblSByte, Suffix[i]).Replace(",", ".");
                }

                return FormatBytes(res.TotalSize);
            }
            catch
            {
                return null;
            }
        }
        #endregion
    }
}

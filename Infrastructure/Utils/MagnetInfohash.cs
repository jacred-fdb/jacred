using MonoTorrent;

namespace JacRed.Infrastructure.Utils
{
    internal static class MagnetInfohash
    {
        internal static bool TryGetHexV1OrV2(string magnet, out string infohash)
        {
            infohash = null;
            if (string.IsNullOrWhiteSpace(magnet))
                return false;

            try
            {
                infohash = Normalize(MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex());
                return IsValidHex40(infohash);
            }
            catch
            {
                return false;
            }
        }

        internal static string Normalize(string infohash) => infohash?.ToLowerInvariant();

        internal static bool IsValidHex40(string infohash)
        {
            if (string.IsNullOrEmpty(infohash) || infohash.Length != 40)
                return false;

            foreach (var c in infohash)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }
    }
}

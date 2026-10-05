using System.Collections.Generic;

namespace JacRed.Models.Api
{
    public class TorrentInfo
    {
        public int quality { get; set; }

        public string videotype { get; set; }

        public HashSet<string> voices { get; set; }

        public HashSet<int> seasons { get; set; }

        public string[] types { get; set; }

        public string sizeName { get; set; }

        public string name { get; set; }

        public string originalname { get; set; }

        public int relased { get; set; }


        public TorrentInfo Clone()
        {
            var clone = (TorrentInfo)MemberwiseClone();

            if (voices != null)
                clone.voices = new HashSet<string>(voices);

            return clone;
        }
    }
}

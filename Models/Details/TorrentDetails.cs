using System;
using System.Collections.Generic;

namespace JacRed.Models.Details
{
    public class TorrentDetails : TorrentBaseDetails, ICloneable
    {
        public double size { get; set; }

        public int quality { get; set; }

        public string videotype { get; set; }

        public HashSet<string> voices { get; set; } = new HashSet<string>();

        public HashSet<int> seasons { get; set; } = new HashSet<int>();


        public object Clone()
        {
            var clone = (TorrentDetails)MemberwiseClone();

            // The source may be a live FileDB cache record: give the clone its own sets,
            // so merging duplicates into the clone does not mutate the cached ones.
            if (voices != null)
                clone.voices = new HashSet<string>(voices);

            if (languages != null)
                clone.languages = new HashSet<string>(languages);

            return clone;
        }
    }
}

using JacRed.Models.Tracks;
using System;
using System.Collections.Generic;

namespace JacRed.Models.Details
{
    public class TorrentBaseDetails
    {
        public string trackerName { get; set; }

        public string[] types { get; set; }

        public string url { get; set; }


        public string title { get; set; }

        public int sid { get; set; }

        public int pir { get; set; }

        public string sizeName { get; set; }

        public DateTime createTime { get; set; } = DateTime.UtcNow;

        public DateTime updateTime { get; set; } = DateTime.UtcNow;

        public DateTime checkTime { get; set; } = DateTime.Now;

        public string magnet { get; set; }


        public string name { get; set; }

        public string originalname { get; set; }

        public int relased { get; set; }


        public HashSet<string> languages { get; set; }

        /// <summary>
        /// Audio languages the tracker states for the release (ISO 639-2), e.g. animelayer "Язык".
        /// Kept apart from <see cref="languages"/>, which updateFullDetails rebuilds, and added to it there.
        /// </summary>
        public HashSet<string> audioLanguages { get; set; }

        /// <summary>Subtitle languages the tracker states for the release (ISO 639-2), e.g. animelayer "Субтитры".</summary>
        public HashSet<string> subtitleLanguages { get; set; }

        public List<ffStream> ffprobe { get; set; }

        public int ffprobe_tryingdata { get; set; }


        public string _sn { get; set; }

        public string _so { get; set; }
    }
}

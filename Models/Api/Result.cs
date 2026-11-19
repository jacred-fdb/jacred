using JacRed.Models.Tracks;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JacRed.Models.Api
{
    public class Result
    {
        public string Tracker { get; set; }

        public string Details { get; set; }

        public string Title { get; set; }

        public double Size { get; set; }

        public DateTime PublishDate { get; set; }

        public HashSet<int> Category { get; set; }

        public string CategoryDesc { get; set; }

        public int Seeders { get; set; }

        public int Peers { get; set; }

        public string MagnetUri { get; set; }

        public List<ffStream> ffprobe { get; set; }

        public HashSet<string> languages { get; set; }

        /// <summary>
        /// Torznab <c>subs</c> values of a v1 record: the subtitle languages its tracker states and those of its
        /// stored ffprobe, which v1 drops with <c>tracks: false</c>. Torznab adds those of <see cref="ffprobe"/>. Not in JSON.
        /// </summary>
        [JsonIgnore]
        public List<string> subs { get; set; }

        /// <summary>
        /// Audio languages the tracker states (<see cref="Details.TorrentBaseDetails.audioLanguages"/>), already in
        /// <see cref="languages"/>: with them Torznab drops its alphabet guess. Not in JSON.
        /// </summary>
        [JsonIgnore]
        public HashSet<string> audioLanguages { get; set; }

        public TorrentInfo info { get; set; }


        public Result Clone()
        {
            var clone = (Result)MemberwiseClone();
            clone.info = info?.Clone();
            return clone;
        }
    }
}

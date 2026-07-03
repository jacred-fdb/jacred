using System.Collections.Generic;

namespace JacRed.Engine.Indexers
{
    public class IndexerSearchRequest
    {
        public string Query { get; set; }

        public string Title { get; set; }

        public string TitleOriginal { get; set; }

        public int Year { get; set; }

        public int IsSerial { get; set; } = -1;

        public string Genres { get; set; }

        public List<int> Categories { get; set; }

        public int? Season { get; set; }

        public int? Episode { get; set; }

        public string Tracker { get; set; }

        public bool CardMode { get; set; }

        public string ApiKey { get; set; }

        public bool RqNum { get; set; }
    }
}

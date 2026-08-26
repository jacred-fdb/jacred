namespace JacRed.Models.Details
{
    /// <summary>SubsPlease release — magnet-only 1080p with API metadata for refresh.</summary>
    public class SubsPleaseDetails : TorrentDetails
    {
        public string showSid { get; set; }
        public string page { get; set; }
        public string episode { get; set; }
        public bool isBatch { get; set; }
        public string infoHash { get; set; }
        public string imageUrl { get; set; }
        public string xdcc { get; set; }
    }
}

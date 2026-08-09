namespace JacRed.Models.Details
{
    public class AnifilmDetails : TorrentDetails
    {
        /// <summary>Relative torrent path, e.g. releases/download-torrent/123.</summary>
        public string downloadId { get; set; }
    }
}

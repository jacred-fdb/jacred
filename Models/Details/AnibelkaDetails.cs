namespace JacRed.Models.Details
{
    public class AnibelkaDetails : TorrentDetails
    {
        /// <summary>Anonymous .torrent attachment id for download/file.php?id=…</summary>
        public string downloadId { get; set; }
    }
}

namespace JacRed.Application.Dev
{
    public interface ITracksAdminService
    {
        object TracksStats(bool includeTorrentDb = true, bool refresh = false);
        object ExportTracks(string dir = "Data/tracks-export", bool dryRun = false, bool includeTorrentDb = true, bool background = true);
        object ExportTracksStatus();
        object BackfillTracks(bool dryRun = false, bool migrateLegacy = true, bool includeTorrentDb = true);
    }
}

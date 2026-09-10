using System.Threading.Tasks;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>Trio ParseAll host — resume after restart without rotating a finished cycle.</summary>
    public interface IParseAllStarter
    {
        string TrackerName { get; }

        Task<string> ParseAllTaskAsync();
    }
}

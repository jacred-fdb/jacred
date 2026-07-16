using System.Collections.Generic;

namespace JacRed.Application.Index
{
    public interface IFastDbIndex
    {
        Dictionary<string, List<string>> Get(bool update = false);

        void Rebuild();

        /// <summary>Resolve masterDb shard keys via fastdb tokens (exact token match or fuzzy Contains).</summary>
        IEnumerable<string> LookupMasterKeys(string sn, string altSn, bool exact, int? take = null);
    }
}

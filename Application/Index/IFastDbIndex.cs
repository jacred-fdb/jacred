using System.Collections.Generic;

namespace JacRed.Application.Index
{
    public interface IFastDbIndex
    {
        Dictionary<string, List<string>> Get(bool update = false);

        void Rebuild();

        bool RebuildIfChanged();

        /// <summary>See <see cref="FastDbIndex.FranchiseKeys"/>.</summary>
        HashSet<string> FranchiseKeys(string nameSn, string originalSn, int max);
    }
}

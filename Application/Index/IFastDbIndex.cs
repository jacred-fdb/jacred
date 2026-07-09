using System.Collections.Generic;

namespace JacRed.Application.Index
{
    public interface IFastDbIndex
    {
        Dictionary<string, List<string>> Get(bool update = false);

        void Rebuild();
    }
}

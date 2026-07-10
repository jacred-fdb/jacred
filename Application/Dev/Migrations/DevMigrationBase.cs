using JacRed.Application.Index;

namespace JacRed.Application.Dev.Migrations
{
  public abstract class DevMigrationBase
  {
    protected readonly IFastDbIndex FastDbIndex;

    protected DevMigrationBase(IFastDbIndex fastDbIndex) => FastDbIndex = fastDbIndex;

    protected void TryRebuildFastDb()
    {
      try { FastDbIndex.Rebuild(); } catch { }
    }
  }
}

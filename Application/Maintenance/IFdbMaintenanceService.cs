namespace JacRed.Application.Maintenance
{
    public interface IFdbMaintenanceService
    {
        /// <summary>Start background integrity job. Returns ok / work.</summary>
        string Check(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true);

        /// <summary>In-progress state + last completed report.</summary>
        object Status();
    }
}

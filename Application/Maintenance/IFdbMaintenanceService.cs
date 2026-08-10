namespace JacRed.Application.Maintenance
{
    public interface IFdbMaintenanceService
    {
        /// <summary>Start background integrity job. Returns ok / work.</summary>
        string Check(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true);

        /// <summary>
        /// Run integrity job synchronously (CLI / tests).
        /// Returns true when the report finished with ok=true.
        /// </summary>
        bool Run(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true,
            System.Threading.CancellationToken cancellationToken = default, bool consoleProgress = false);

        /// <summary>In-progress state + last completed report.</summary>
        object Status();
    }
}

using JacRed.Application.Maintenance;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/maintenance/[action]")]
    public class MaintenanceController : Controller
    {
        readonly IFdbMaintenanceService _maintenanceService;

        public MaintenanceController(IFdbMaintenanceService maintenanceService)
        {
            _maintenanceService = maintenanceService;
        }

        /// <summary>
        /// Start FDB integrity check. Returns ok / work.
        /// mode=report|safe|full (default report), sampleSize, excludeNumericXx.
        /// </summary>
        public string Check(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true)
            => _maintenanceService.Check(mode, sampleSize, excludeNumericXx);

        /// <summary>In-progress state and last completed report.</summary>
        public JsonResult Status() => Json(_maintenanceService.Status());
    }
}

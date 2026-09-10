using System.Threading.Tasks;
using JacRed.Application.Maintenance;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/maintenance/[action]")]
    public class MaintenanceController : Controller
    {
        readonly IFdbMaintenanceService _maintenanceService;
        readonly ParseAllResumeService _parseAllResume;

        public MaintenanceController(IFdbMaintenanceService maintenanceService, ParseAllResumeService parseAllResume)
        {
            _maintenanceService = maintenanceService;
            _parseAllResume = parseAllResume;
        }

        /// <summary>
        /// Start FDB integrity check. Returns ok / work.
        /// mode=report|safe|full (default report), sampleSize, excludeNumericXx.
        /// </summary>
        public string Check(string mode = "report", int sampleSize = 20, bool excludeNumericXx = true)
            => _maintenanceService.Check(mode, sampleSize, excludeNumericXx);

        /// <summary>In-progress state and last completed report.</summary>
        public JsonResult Status() => Json(_maintenanceService.Status());

        /// <summary>
        /// Continue incomplete ParseAll cycles after restart. Does not start a new cycle
        /// when pending is 0. Returns work if already running.
        /// </summary>
        public async Task<JsonResult> ResumeParseAll()
            => Json(await _parseAllResume.ResumeAsync());

        /// <summary>Cycle pending vs running ParseAll for trio trackers.</summary>
        public JsonResult ParseAllStatus() => Json(_parseAllResume.Status());
    }
}

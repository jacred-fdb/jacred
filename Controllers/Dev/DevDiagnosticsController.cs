using JacRed.Application.Dev;
using Microsoft.AspNetCore.Mvc;
using JacRed.Controllers.Filters;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
    [LocalhostOnly]
    public class DevDiagnosticsController : Controller
    {
        readonly IDevDiagnosticsService _diagnosticsService;

        public DevDiagnosticsController(IDevDiagnosticsService diagnosticsService)
        {
            _diagnosticsService = diagnosticsService;
        }

        public JsonResult FindCorrupt(int sampleSize = 20) => Json(_diagnosticsService.FindCorrupt(sampleSize));

        public JsonResult FindDuplicateKeys(string tracker = null, bool excludeNumeric = true) =>
            Json(_diagnosticsService.FindDuplicateKeys(tracker, excludeNumeric));

        public JsonResult FindEmptySearchFields(int sampleSize = 20) => Json(_diagnosticsService.FindEmptySearchFields(sampleSize));
    }
}

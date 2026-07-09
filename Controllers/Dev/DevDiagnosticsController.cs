using JacRed.Application.Dev;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
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

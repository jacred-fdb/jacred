using JacRed.Application.Dev;
using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
    [JacRedAuthorize(JacRedAccessPolicy.DevAdmin)]
    public class DevMaintenanceController : Controller
    {
        readonly IDevMaintenanceService _maintenanceService;

        public DevMaintenanceController(IDevMaintenanceService maintenanceService)
        {
            _maintenanceService = maintenanceService;
        }

        public JsonResult UpdateSize() => Json(_maintenanceService.UpdateSize());

        public JsonResult ResetCheckTime() => Json(_maintenanceService.ResetCheckTime());

        public JsonResult UpdateDetails() => Json(_maintenanceService.UpdateDetails());

        public JsonResult UpdateSearchName() => Json(_maintenanceService.UpdateSearchName());
    }
}

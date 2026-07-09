using JacRed.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Controllers
{
    public class HealthController : Controller
    {
        [Route("health")]
        public IActionResult Health()
        {
            return Json(new Dictionary<string, string>
            {
                ["status"] = "OK"
            });
        }

        [Route("version")]
        public IActionResult Version()
        {
            return Json(new Dictionary<string, string>
            {
                ["version"] = VersionInfo.Version,
                ["gitSha"] = VersionInfo.GitSha,
                ["gitBranch"] = VersionInfo.GitBranch,
                ["buildDate"] = VersionInfo.BuildDate
            });
        }

        [Route("lastupdatedb")]
        public IActionResult LastUpdateDB()
        {
            string lastUpdate = "01.01.2000 01:01";
            if (FileDB.masterDb != null && FileDB.masterDb.Count > 0)
                lastUpdate = FileDB.masterDb.OrderByDescending(i => i.Value.updateTime).First().Value.updateTime.ToString("dd.MM.yyyy HH:mm");

            return Json(new Dictionary<string, string>
            {
                ["lastupdatedb"] = lastUpdate
            });
        }

        [Route("api/v1.0/conf")]
        public JsonResult JacRedConf([FromQuery] string apikey = null)
        {
            var provided = !string.IsNullOrWhiteSpace(apikey)
                ? apikey.Trim()
                : JacRedKeyUtils.GetApiKeyFromRequest(HttpContext);
            var configured = AppInit.conf?.apikey;
            return Json(new
            {
                apikey = string.IsNullOrWhiteSpace(configured) || JacRedKeyUtils.SecureEquals(provided, configured)
            });
        }
    }
}

using JacRed.Infrastructure.Security;
using JacRed.Infrastructure.Trackers;
using Microsoft.AspNetCore.Mvc;
using System;
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

        /// <summary>In-process background ParseAll / UpdateTasks jobs (local ops).</summary>
        [Route("health/background-jobs")]
        public IActionResult BackgroundJobs()
        {
            var jobs = TrackerSyncHelpers.GetActiveJobs().Select(j => new
            {
                j.Key,
                j.Tracker,
                j.JobLabel,
                startedAtUtc = j.StartedAtUtc.ToString("o"),
                ageSec = Math.Max(0, (int)(DateTime.UtcNow - j.StartedAtUtc).TotalSeconds),
                progressCurrent = j.ProgressCurrent,
                progressTotal = j.ProgressTotal,
                j.ProgressDetail
            });
            return Json(new { jobs });
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
            var configuredKey = AppInit.conf?.apikey;
            var isConfigured = !string.IsNullOrWhiteSpace(configuredKey);
            return Json(new
            {
                jacred = true,
                configured = isConfigured,
                apikey = !isConfigured || JacRedKeyUtils.SecureEquals(provided, configuredKey),
                version = VersionInfo.Version
            });
        }
    }
}

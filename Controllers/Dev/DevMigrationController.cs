using JacRed.Application.Dev;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.Dev
{
    [Route("/dev/[action]")]
    public class DevMigrationController : Controller
    {
        readonly IDevMigrationService _migrationService;

        public DevMigrationController(IDevMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        public JsonResult FixKnabenNames() => Json(_migrationService.FixKnabenNames());

        public JsonResult FixBitruNames() => Json(_migrationService.FixBitruNames());

        public JsonResult RemoveNullValues() => Json(_migrationService.RemoveNullValues());

        public JsonResult RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null) =>
            Json(_migrationService.RemoveBucket(key, migrateName, migrateOriginalname));

        public JsonResult FixEmptySearchFields() => Json(_migrationService.FixEmptySearchFields());

        public JsonResult MigrateAnilibertyUrls() => Json(_migrationService.MigrateAnilibertyUrls());

        public JsonResult RemoveDuplicateAniliberty() => Json(_migrationService.RemoveDuplicateAniliberty());

        public JsonResult FixAnimelayerDuplicates() => Json(_migrationService.FixAnimelayerDuplicates());
    }
}

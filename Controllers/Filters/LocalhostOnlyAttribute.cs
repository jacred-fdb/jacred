using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JacRed.Controllers.Filters
{
    /// <summary>
    /// Restricts action to loopback clients (127.0.0.1). Used on Dev controllers.
    /// </summary>
    /// <example>
    /// <code>
    /// [LocalhostOnly]
    /// public JsonResult UpdateSize() { ... }
    /// </code>
    /// </example>
    public class LocalhostOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                context.Result = new JsonResult(new { badip = true });
        }
    }
}

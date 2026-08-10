using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Прогрев проверки Cloudflare через FlareSolverr.
    /// Решение задачи занимает до ~80–180 с и грузит CPU — лучше вызывать
    /// отдельным cron за несколько минут до обхода Rutracker.
    /// </summary>
    [Route("/cron/cloudflare/[action]")]
    public class CloudflareController : BaseController
    {
        public CloudflareController(IMemoryCache memoryCache) : base(memoryCache)
        {
        }

        /// <summary>
        /// Открывает браузером указанный адрес, чтобы сессия была готова к обходу.
        /// По умолчанию — rutracker tracker search (надёжный CF-триггер «Just a moment…»).
        /// </summary>
        /// <param name="url">CF-триггер. По умолчанию tracker search — надёжно ловит «Just a moment…».</param>
        async public Task<IActionResult> Warmup(string url = "https://rutracker.org/forum/tracker.php?nm=")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string host = null;
            try { host = new System.Uri(url).Host; } catch (System.UriFormatException) { }

            string html = await CloudflareClearance.FetchAsync(url);
            bool ok = !string.IsNullOrWhiteSpace(html);

            // Помечаем хост только после успешного прогрева — иначе при падении
            // FlareSolverr все GET уйдут в браузер на hours (guardedHours).
            if (ok)
                CloudflareClearance.MarkGuarded(host);
            else
                CloudflareClearance.Unguard(host);

            return Json(new
            {
                ok,
                host,
                length = html?.Length ?? 0,
                tookSeconds = System.Math.Round(sw.Elapsed.TotalSeconds, 1)
            });
        }
    }
}

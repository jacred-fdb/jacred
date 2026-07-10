using JacRed.Infrastructure.Stats;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Http
{
    public sealed class StreamingStatsTorrentsResult : IActionResult
    {
        readonly StatsTorrentIndex.Query _query;

        public StreamingStatsTorrentsResult(StatsTorrentIndex.Query query) => _query = query;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "application/json; charset=utf-8";

            var meta = StatsTorrentIndex.TryLoadMeta();
            if (meta != null)
                response.Headers["X-Stats-Index-At"] = meta.updatedAt.ToString("O");

            await StatsTorrentIndex.WriteResponseAsync(response.Body, _query, context.HttpContext.RequestAborted);
        }
    }
}

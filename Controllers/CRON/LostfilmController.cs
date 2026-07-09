using System.Threading.Tasks;
using JacRed.Engine.Trackers.Lostfilm;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.CRON
{
    [Route("/cron/lostfilm/[action]")]
    public class LostfilmController : Controller
    {
        readonly LostfilmSyncService _syncService;

        public LostfilmController(LostfilmSyncService syncService)
        {
            _syncService = syncService;
        }

        /// <summary>Парсит только первую страницу /new/ — актуальные новинки.</summary>
        [HttpGet]
        public async Task<string> Parse()
        {
            return await _syncService.ParseAsync();
        }

        /// <summary>Парсит страницы /new/ в указанном диапазоне. Без кэша, без фильтров по датам.</summary>
        /// <param name="pageFrom">Начальная страница (1 = /new/, 2 = /new/page_2, ...)</param>
        /// <param name="pageTo">Конечная страница включительно. Если больше реального числа страниц — обрезается.</param>
        [HttpGet]
        public async Task<string> ParsePages(int pageFrom = 1, int pageTo = 1)
        {
            return await _syncService.ParsePagesAsync(pageFrom, pageTo);
        }

        /// <summary>Парсит страницу /series/{series}/seasons/ и добавляет торренты «полный сезон» (SD, 1080p, 720p) для каждого сезона с e=999.</summary>
        /// <param name="series">Slug сериала, например Outer_Banks</param>
        [HttpGet]
        public async Task<string> ParseSeasonPacks(string series)
        {
            return await _syncService.ParseSeasonPacksAsync(series);
        }

        /// <summary>Запрашивает /new/, парсит даты и возвращает, что мы извлекаем (dateStr, relased). Для проверки года в заголовках. Опционально ?series=slug фильтрует по сериалу (например Drops_of_God).</summary>
        [HttpGet]
        public async Task<IActionResult> VerifyPage(string series = null)
        {
            return Json(await _syncService.VerifyPageAsync(series));
        }

        /// <summary>Статистика по раздачам lostfilm в базе: количество, с магнитом, примеры ключей.</summary>
        [HttpGet]
        public IActionResult Stats()
        {
            return Json(_syncService.GetStats());
        }
    }
}

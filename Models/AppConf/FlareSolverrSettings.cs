namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Прохождение проверки Cloudflare через FlareSolverr — безголовый браузер,
    /// который стоит рядом в compose и не публикуется наружу.
    ///
    /// Дешёвый путь «забрать cookie и ходить дальше обычным клиентом» проверен
    /// и не работает: с той же cookie и тем же User-Agent прилетает 403 —
    /// Cloudflare сверяет ещё и отпечаток TLS. Поэтому такие хосты обслуживает
    /// браузер целиком, в одной постоянной сессии.
    /// </summary>
    public class FlareSolverrSettings
    {
        public bool enable { get; set; } = true;

        /// <summary>
        /// Адрес службы.
        /// Host-run JacRed + published FlareSolverr: http://127.0.0.1:8191/v1
        /// JacRed in docker-compose: http://flaresolverr:8191/v1
        /// </summary>
        public string url { get; set; } = "http://127.0.0.1:8191/v1";

        /// <summary>
        /// Сколько ждать ответа браузера, мс. Первое обращение долгое — там
        /// решается задача: на rutracker замерено около 80 секунд.
        /// </summary>
        public int maxTimeoutMs { get; set; } = 180000;

        /// <summary>
        /// Через сколько минут простоя закрывать сессию браузера (~700 МБ).
        /// </summary>
        public int sessionIdleMinutes { get; set; } = 30;

        /// <summary>
        /// Сколько часов помнить, что хост закрыт проверкой, и ходить туда
        /// сразу браузером, не тратя запрос на заведомый отказ.
        /// </summary>
        public int guardedHours { get; set; } = 6;

        /// <summary>
        /// Как часто давать закрытому хосту шанс ответить обычным путём.
        /// </summary>
        public int recheckMinutes { get; set; } = 30;
    }
}

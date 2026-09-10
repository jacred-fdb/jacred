namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Прохождение проверки Cloudflare через FlareSolverr — безголовый браузер,
    /// который стоит рядом в compose и не публикуется наружу.
    ///
    /// Cookie <c>cf_clearance</c> нельзя отдать обычному .NET HttpClient (другой TLS).
    /// После solve страницы забирает <c>cffetch</c> (curl_cffi). Браузер — fallback
    /// и обновление jar. У каждого guarded-хоста своя сессия Chromium.
    ///
    /// Proxy для Chromium настраивается у контейнера FlareSolverr
    /// (<c>PROXY_URL</c> / <c>PROXY_USERNAME</c> / <c>PROXY_PASSWORD</c>), не здесь.
    /// Тот же SOCKS нужно указать в <c>cffetch.proxy</c> / <c>CFFETCH_PROXY</c>.
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
        /// Separate FlareSolverr for ParseAll / UpdateTasks / ParseLatest.
        /// Empty — same instance as <see cref="url"/>. Host-run crawl: :8193.
        /// Several sessions in one FlareSolverr do not add throughput; two
        /// instances do. Hourly parse stays on <see cref="url"/>.
        /// </summary>
        public string crawlUrl { get; set; } = "";

        /// <summary>
        /// Сколько ждать ответа браузера, мс. Первое обращение долгое — там
        /// решается задача: на rutracker замерено около 80 секунд.
        /// </summary>
        public int maxTimeoutMs { get; set; } = 300000;

        /// <summary>
        /// Через сколько минут простоя закрывать сессию браузера (~700 МБ).
        /// </summary>
        public int sessionIdleMinutes { get; set; } = 120;

        /// <summary>
        /// Повторы того же request.get в той же сессии при browser timeout
        /// (chromedriver hang), до destroy.
        /// </summary>
        public int browserTimeoutRetries { get; set; } = 1;

        /// <summary>
        /// Сколько подряд browser timeout’ов терпеть, прежде чем destroy+create.
        /// </summary>
        public int recycleAfterTimeouts { get; set; } = 3;

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

namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Быстрый путь к CF-хостам: cookie от FlareSolverr + TLS Chrome (curl_cffi).
    /// .NET HttpClient с той же cookie даёт 403 — другой отпечаток.
    /// Образ <c>ghcr.io/jacred-fdb/cffetch</c>, host network, :8192.
    /// Замер master 2026-09-10: ~0.18 с vs ~0.5–1.2 с через page.goto.
    /// </summary>
    public class CfFetchSettings
    {
        public bool enable { get; set; } = true;

        /// <summary>
        /// Host-run JacRed: <c>http://127.0.0.1:8192/fetch</c>.
        /// Compose (JacRed на bridge, cffetch host network):
        /// <c>http://host.docker.internal:8192/fetch</c>.
        /// </summary>
        public string url { get; set; } = "http://127.0.0.1:8192/fetch";

        /// <summary>
        /// Профиль curl_cffi. Не старше Chromium FlareSolverr (на master — 148;
        /// chrome136 проходит). chrome116 при сессии 148 даёт cf-mitigated.
        /// </summary>
        public string impersonate { get; set; } = "chrome136";

        public int timeoutSeconds { get; set; } = 25;

        /// <summary>Без этого обход упирается в 429 трекера, не в браузер.</summary>
        public int maxConcurrent { get; set; } = 4;

        /// <summary>Минут доверять jar; отказ уводит на браузер за свежей cookie.</summary>
        public int clearanceMinutes { get; set; } = 60;

        /// <summary>
        /// Тот же SOCKS, что PROXY_URL у FlareSolverr (WARP). Иначе IP-binding
        /// clearance. Пусто — cffetch берёт CFFETCH_PROXY из окружения.
        /// </summary>
        public string proxy { get; set; } = "";
    }
}

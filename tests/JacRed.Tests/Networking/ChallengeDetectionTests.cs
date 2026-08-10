using System.Net;
using System.Net.Http;
using JacRed.Infrastructure.Networking;
using Xunit;

namespace JacRed.Tests.Networking;

/// <summary>
/// Отличаем вызов Cloudflare от обычного отказа трекера.
///
/// Цена ошибки несимметрична. Не распознали проверку — потеряли одну
/// страницу. Приняли обычный отказ за проверку — хост уходит в браузер
/// на часы, и обход встаёт целиком.
///
/// Признак — <c>cf-mitigated</c>, не <c>cf-ray</c> (cf-ray стоит на каждом
/// ответе сайта за Cloudflare, включая обычный 200).
/// </summary>
public class ChallengeDetectionTests
{
    static HttpResponseMessage Response(HttpStatusCode code, params (string name, string value)[] headers)
    {
        var r = new HttpResponseMessage(code);
        foreach (var (name, value) in headers)
            r.Headers.TryAddWithoutValidation(name, value);
        return r;
    }

    [Fact]
    public void Cf_ray_сам_по_себе_проверкой_не_считается()
    {
        Assert.False(IsChallenge(Response(HttpStatusCode.ServiceUnavailable, ("cf-ray", "a23b3a1cf9f8dbcb-FRA"))));
        Assert.False(IsChallenge(Response(HttpStatusCode.Forbidden, ("cf-ray", "a23b3a1cf9f8dbcb-FRA"))));
    }

    [Fact]
    public void Cf_mitigated_считается()
    {
        Assert.True(IsChallenge(Response(HttpStatusCode.Forbidden, ("cf-mitigated", "challenge"))));
        Assert.True(IsChallenge(Response(HttpStatusCode.ServiceUnavailable, ("cf-mitigated", "challenge"))));
    }

    [Fact]
    public void Успешный_ответ_проверкой_не_считается()
    {
        Assert.False(IsChallenge(Response(HttpStatusCode.OK, ("cf-mitigated", "challenge"))));
        Assert.False(IsChallenge(null));
    }

    [Theory]
    [InlineData("<html><head><title>Just a moment...</title></head></html>")]
    [InlineData("<html><head><title>Один момент...</title></head></html>")]
    [InlineData("<div class=\"cf-browser-verification\"></div>")]
    [InlineData("window._cf_chl_opt = {}")]
    [InlineData("/cdn-cgi/challenge-platform/h/b/orchestrate/chl_page/v1")]
    public void Разметка_задачи_в_теле_считается(string body)
    {
        Assert.True(CloudflareClearance.IsChallengeBody(body));
    }

    [Theory]
    [InlineData("<html><body>Форум временно недоступен</body></html>")]
    [InlineData("504 Gateway Time-out")]
    [InlineData("")]
    [InlineData(null)]
    // Обычная выдача rutracker: Cloudflare jsd, не interstitial.
    [InlineData("a.src='/cdn-cgi/challenge-platform/scripts/jsd/main.js';")]
    public void Обычная_страница_отказа_проверкой_не_считается(string body)
    {
        Assert.False(CloudflareClearance.IsChallengeBody(body));
    }

    [Fact]
    public void Реальная_выдача_с_jsd_не_проверка()
    {
        // Фрагмент с живой страницы viewforum (FlareSolverr 200 + torTopic).
        string body = "<title>Фильмы до 1990 года</title>"
            + "a.src='/cdn-cgi/challenge-platform/scripts/jsd/main.js';"
            + "class=\"torTopic\" id=\"tt-123\"";
        Assert.False(CloudflareClearance.IsChallengeBody(body));
    }

    [Fact]
    public void Большое_тело_не_разбираем()
    {
        Assert.False(CloudflareClearance.IsChallengeBody(new string('a', 300_000) + "Just a moment"));
    }

    static bool IsChallenge(HttpResponseMessage r) => CloudflareClearance.IsChallenge(r);
}

using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Security;

namespace JacRed.Controllers
{
    public class HomeController : Controller
    {
        [Route("/")]
        public ActionResult Index()
        {
            SetNoStoreHtmlHeaders(Response);
            return File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");
        }

        [Route("/stats")]
        public ActionResult Stats()
        {
            SetNoStoreHtmlHeaders(Response);
            return File(System.IO.File.OpenRead("wwwroot/stats.html"), "text/html");
        }

        [Route("/settings")]
        public ActionResult Settings()
        {
            SetNoStoreHtmlHeaders(Response);
            return File(System.IO.File.OpenRead("wwwroot/settings.html"), "text/html");
        }

        static void SetNoStoreHtmlHeaders(Microsoft.AspNetCore.Http.HttpResponse response)
        {
            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";
        }

        [Route("/opensearch.xml")]
        public ContentResult OpenSearch()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var searchTemplate = $"{baseUrl}/?s={{searchTerms}}";
            var iconUrl = $"{baseUrl}/img/jacred.png";

            var xml = string.Join('\n',
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<OpenSearchDescription xmlns=\"http://a9.com/-/spec/opensearch/1.1/\">",
                "  <ShortName>JacRed</ShortName>",
                "  <Description>Поиск торрентов JacRed</Description>",
                "  <InputEncoding>UTF-8</InputEncoding>",
                $"  <Image width=\"32\" height=\"32\" type=\"image/png\">{SecurityElement.Escape(iconUrl)}</Image>",
                $"  <Url type=\"text/html\" method=\"get\" template=\"{SecurityElement.Escape(searchTemplate)}\"/>",
                "</OpenSearchDescription>");

            return Content(xml, "application/opensearchdescription+xml", Encoding.UTF8);
        }
    }
}

using Microsoft.AspNetCore.Http;

namespace JacRed.Application.Search
{
    public class JackettSearchRequest
    {
        public IQueryCollection Query { get; set; }
        public string QueryStringValue { get; set; }
        public string UserAgent { get; set; }
        public string ApiKey { get; set; }
        public string QueryText { get; set; }
        public string Title { get; set; }
        public string TitleOriginal { get; set; }
        public int Year { get; set; }
        public int IsSerial { get; set; } = -1;
    }
}

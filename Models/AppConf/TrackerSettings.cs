namespace JacRed.Models.AppConf
{
    public class TrackerSettings
    {
        public TrackerSettings(string host, bool useproxy = false, LoginSettings login = null, int reqMinute = 8)
        {
            this.host = host;
            this.useproxy = useproxy;
            this.reqMinute = reqMinute;

            if (login != null)
                this.login = login;
        }


        public string host { get; }

        public string alias { get; set; }

        public string rqHost(string uri = null)
        {
            if (uri == null)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    return alias;

                return host;
            }

            if (string.IsNullOrWhiteSpace(alias))
                return uri;

            return uri.Replace(host, alias);
        }


        public string cookie { get; set; }

        /// <summary>When true and parser log is enabled (logParsers or log), parser writes to Data/log/{tracker}.log</summary>
        public bool log { get; set; }

        public bool useproxy { get; set; }

        public int reqMinute { get; set; }

        public int parseDelay
        {
            get
            {
                if (reqMinute == -1)
                    return 10;

                if (reqMinute >= 60)
                    return 1000;

                if (reqMinute <= 0)
                    return 60_000;

                return (60 / reqMinute) * 1000;
            }
        }

        public LoginSettings login { get; set; } = new LoginSettings();
    }
}

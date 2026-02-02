using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JacRed.Engine
{
    public static class TrackersCron
    {
        async public static Task Run()
        {
            await Task.Delay(20_000);

            while (true)
            {
                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    continue;
                }

                await Task.Delay(TimeSpan.FromHours(1));

                try
                {
                    Console.WriteLine($"trackers: start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    HashSet<string> trackers = new HashSet<string>();

                    foreach (var item in FileDB.masterDb.ToArray())
                    {
                        foreach (var t in FileDB.OpenRead(item.Key/*, cache: false*/).Values)
                        {
                            if (string.IsNullOrEmpty(t.magnet))
                                continue;

                            try
                            {
                                if (t.magnet.Contains("&"))
                                {
                                    foreach (Match tr in Regex.Matches(t.magnet, "tr=([^&]+)"))
                                    {
                                        string tracker = HttpUtility.UrlDecode(tr.Groups[1].Value.Split("?")[0]).Trim().ToLower();
                                        if (string.IsNullOrWhiteSpace(tracker) || tracker.Contains("[") || !tracker.Replace("://", "").Contains(":") || tracker.Contains(" ") || tracker.Contains("torrentsmd.eu"))
                                            continue;

                                        if (Regex.IsMatch(tracker, "[^/]+/[^/]+/announce"))
                                            continue;

                                        if (await ckeck(tracker))
                                            trackers.Add(tracker);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    File.WriteAllLines("wwwroot/trackers.txt", trackers);
                    Console.WriteLine($"trackers: end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} wrote {trackers.Count} trackers to wwwroot/trackers.txt");
                }
                catch (Exception ex) { Console.WriteLine($"trackers: error / {ex.Message}"); }
            }
        }


        async static Task<bool> ckeck(string tracker)
        {
            if (string.IsNullOrWhiteSpace(tracker) || tracker.Contains("["))
                return false;

            if (tracker.StartsWith("http"))
            {
                try
                {
                    using (var handler = new System.Net.Http.HttpClientHandler())
                    {
                        handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

                        using (var client = new System.Net.Http.HttpClient(handler))
                        {
                            client.Timeout = TimeSpan.FromSeconds(7);
                            await client.GetAsync(tracker, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                            return true;
                        }
                    }
                }
                catch { }
            }
            else if (tracker.StartsWith("udp:"))
            {
                try
                {
                    tracker = tracker.Replace("udp://", "");

                    string host = tracker.Split(':')[0].Split('/')[0];
                    int port = tracker.Contains(":") ? int.Parse(tracker.Split(':')[1].Split('/')[0]) : 6969;

                    using (UdpClient client = new UdpClient(host, port))
                    {
                        CancellationTokenSource cts = new CancellationTokenSource();
                        cts.CancelAfter(7000);

                        string uri = Regex.Match(tracker, "^[^/]/(.*)").Groups[1].Value;
                        await client.SendAsync(Encoding.UTF8.GetBytes($"GET /{uri} HTTP/1.1\r\nHost: {host}\r\n\r\n"), cts.Token);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }
    }
}

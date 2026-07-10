using JacRed.Infrastructure.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Tracks
{
    internal static class TracksLogging
    {
        internal static void Log(string message, int? typetask = null, LogLevel? level = null)
        {
            var logLevel = level ?? JacRedLog.ClassifyTracksMessage(message);
            if (logLevel == LogLevel.Debug && !JacRedLogSettings.TracksConsoleDetail)
            {
                if (AppInit.conf?.trackslog == true)
                    LogToFile(message, typetask);
                return;
            }

            if (!JacRedLogSettings.TracksConsoleDetail && logLevel == LogLevel.Warning
                && !message.Contains("без результата", StringComparison.Ordinal))
            {
                if (AppInit.conf?.trackslog == true)
                    LogToFile(message, typetask);
                return;
            }

            string timeNow = DateTime.Now.ToString("HH:mm:ss");
            string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
            string body = $"[{timeNow}]{typetaskInfo} {message}";

            JacRedLog.Write(JacRedLogCategories.Tracks, logLevel, body);

            if (AppInit.conf?.trackslog == true)
                LogToFile(message, typetask);
        }

        internal static void LogToFile(string message, int? typetask = null)
        {
            try
            {
                string logDir = "Data/log";
                string logFile = Path.Combine(logDir, "tracks.log");

                Directory.CreateDirectory(logDir);

                string timeNow = DateTime.Now.ToString("HH:mm:ss");
                string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
                string logMessage = $"tracks: [{timeNow}]{typetaskInfo} {message}{Environment.NewLine}";

                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using (var stream = new FileStream(
                            logFile,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite))
                        using (var writer = new StreamWriter(stream, Encoding.UTF8))
                        {
                            writer.Write(logMessage);
                        }
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string timeNow = DateTime.Now.ToString("HH:mm:ss");
                    JacRedLog.Error(JacRedLogCategories.Tracks, $"[{timeNow}] Ошибка записи в лог файл: {ex.Message}");
                }
                catch { }
            }
        }
    }
}

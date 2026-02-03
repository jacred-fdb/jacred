using System;
using System.IO;

namespace JacRed.Engine
{
    public static class ParserLog
    {
        const string LogDir = "Data/log";

        public static void Write(string trackerName, string message)
        {
            if (!AppInit.TrackerLogEnabled(trackerName))
                return;

            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                string logPath = Path.Combine(LogDir, $"{trackerName}.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }
}

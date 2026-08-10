using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace JacRed.Infrastructure.Persistence
{
    public static class JsonStream
    {
        static readonly ConcurrentDictionary<string, object> _pathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        static readonly TimeSpan SlowWriteWarn = TimeSpan.FromSeconds(5);

        static object LockFor(string path)
        {
            string key;
            try
            {
                key = Path.GetFullPath(path);
            }
            catch
            {
                key = path ?? string.Empty;
            }

            return _pathLocks.GetOrAdd(key, _ => new object());
        }

        #region Read
        public static T Read<T>(string path)
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Error = (se, ev) => { ev.ErrorContext.Handled = true; }
                };

                var serializer = JsonSerializer.Create(settings);

                using (Stream file = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
                {
                    using (var sr = new StreamReader(file))
                    {
                        using (var jsonTextReader = new JsonTextReader(sr))
                        {
                            return serializer.Deserialize<T>(jsonTextReader);
                        }
                    }
                }
            }
            catch { return default; }
        }
        #endregion

        #region Write
        public static void Write(string path, object db)
        {
            var gate = LockFor(path);
            lock (gate)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var serializer = JsonSerializer.Create();
                    var tempPath = path + ".tmp";

                    using (var streamWriter = new StreamWriter(new GZipStream(File.Create(tempPath), CompressionMode.Compress)))
                    {
                        using (var jsonTextWriter = new JsonTextWriter(streamWriter))
                        {
                            serializer.Serialize(jsonTextWriter, db);
                        }
                    }

                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);
                }
                catch { }
                finally
                {
                    sw.Stop();
                    if (sw.Elapsed > SlowWriteWarn)
                    {
                        JacRedLog.Warning(JacRedLogCategories.Fdb,
                            $"JsonStream.Write slow path={path} elapsed={sw.Elapsed.TotalSeconds:F1}s");
                    }
                }
            }
        }
        #endregion
    }
}

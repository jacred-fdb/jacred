using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;

namespace JacRed.Infrastructure.Persistence
{
    public static class JsonStream
    {
        static readonly object _writeLock = new object();
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
            lock (_writeLock)
            {
                try
                {
                    var serializer = JsonSerializer.Create();
                    var tempPath = path + ".tmp";

                    using (var sw = new StreamWriter(new GZipStream(File.Create(tempPath), CompressionMode.Compress)))
                    {
                        using (var jsonTextWriter = new JsonTextWriter(sw))
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
            }
        }
        #endregion
    }
}

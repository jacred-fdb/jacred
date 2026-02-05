using JacRed.Engine.CORE;
using JacRed.Models.Details;
using JacRed.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JacRed.Engine
{
    public static class TracksDB
    {
        public static void Configuration()
        {
            Console.WriteLine("TracksDB load");

            foreach (var folder1 in Directory.GetDirectories("Data/tracks"))
            {
                foreach (var folder2 in Directory.GetDirectories(folder1))
                {
                    foreach (var file in Directory.GetFiles(folder2))
                    {
                        string infohash = folder1.Substring(12) + folder2.Substring(folder1.Length + 1) + Path.GetFileName(file);

                        try
                        {
                            var res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(file));
                            if (res?.streams != null && res.streams.Count > 0)
                                Database.TryAdd(infohash, res);
                        }
                        catch { }
                    }
                }
            }
        }

        static Random random = new Random();

        static ConcurrentDictionary<string, FfprobeModel> Database = new ConcurrentDictionary<string, FfprobeModel>();

        static string pathDb(string infohash, bool createfolder = false)
        {
            string folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

            if (createfolder)
                Directory.CreateDirectory(folder);

            return $"{folder}/{infohash.Substring(3)}";
        }

        public static bool theBad(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
                return true;

            return false;
        }

        public static List<ffStream> Get(string magnet, string[] types = null, bool onlydb = false)
        {
            if (types != null && theBad(types))
                return null;

            string infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
            if (Database.TryGetValue(infohash, out FfprobeModel res))
                return res.streams;

            string path = pathDb(infohash);
            if (!File.Exists(path))
                return null;

            try
            {
                res = JsonConvert.DeserializeObject<FfprobeModel>(File.ReadAllText(path));
                if (res?.streams == null || res.streams.Count == 0)
                    return null;
            }
            catch { return null; }

            Database.AddOrUpdate(infohash, res, (k, v) => res);
            return res.streams;
        }



///Fix by lexandr0s

		async public static Task Add(string magnet, string[] types = null)
		{
			// Логирование в файл
			void LogToFile(string message)
			{
				try
				{
					/*
					// Безопасная проверка конфигурации
					if (AppInit.conf == null)
						return;
						
					// Проверяем свойство trackslog безопасным способом
					bool tracksLogEnabled = false;
					try
					{
						var prop = AppInit.conf.GetType().GetProperty("trackslog");
						if (prop != null && prop.PropertyType == typeof(bool))
						{
							var value = prop.GetValue(AppInit.conf);
							if (value != null)
								tracksLogEnabled = (bool)value;
						}
					}
					catch
					{
						// Если свойство не найдено или не bool, выходим
						return;
					}
					*/
					
					//if (!tracksLogEnabled)
					if (!AppInit.conf.trackslog)
						return;
					
					string logDir = "Data/log";  // Изменено с Data/temp на Data/log
					string logFile = Path.Combine(logDir, "tracks.log");
					
					try
					{
						// Создаем директорию если не существует
						if (!Directory.Exists(logDir))
						{
							Directory.CreateDirectory(logDir);
							// Даем время на создание директории
							Thread.Sleep(10);
						}
						
						// Форматируем сообщение в формате tracks: [время] сообщение
						string timeNow = DateTime.Now.ToString("HH:mm:ss");
						string logMessage = $"tracks: [{timeNow}] {message}{Environment.NewLine}";
						
						// Записываем в файл с безопасной обработкой блокировок
						for (int i = 0; i < 3; i++) // 3 попытки
						{
							try
							{
								File.AppendAllText(logFile, logMessage, Encoding.UTF8);
								break; // Успешно, выходим
							}
							catch (IOException) when (i < 2) // Если файл заблокирован
							{
								Thread.Sleep(50); // Ждем немного перед повторной попыткой
							}
						}
					}
					catch (Exception ex)
					{
						// Для ошибок записи в лог также используем формат tracks:
						string timeNow = DateTime.Now.ToString("HH:mm:ss");
						Console.WriteLine($"tracks: [{timeNow}] Ошибка записи в лог файл: {ex.Message}");
					}
				}
				catch (Exception ex)
				{
					// Абсолютно безопасный вывод
					try 
					{ 
						string timeNow = DateTime.Now.ToString("HH:mm:ss");
						Console.WriteLine($"tracks: [{timeNow}] Критическая ошибка в LogToFile: {ex.Message}"); 
					} 
					catch { }
				}
			}

			// Функция для логирования с префиксом tracks: и временем в квадратных скобках
			void Log(string message)
			{
				string timeNow = DateTime.Now.ToString("HH:mm:ss");
				string fullMessage = $"tracks: [{timeNow}] {message}";
				Console.WriteLine(fullMessage);
				LogToFile(message);
			}

			if (types != null && theBad(types))
			{
				string msg = $"Пропуск добавления треков: недопустимый тип контента [{string.Join(",", types)}]";
				Log(msg);
				return;
			}

			if (AppInit.conf?.tsuri == null || AppInit.conf.tsuri.Length == 0)
			{
				string msg = "Ошибка: не настроены tsuri серверы";
				Log(msg);
				return;
			}

			string infohash = MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
			if (string.IsNullOrEmpty(infohash))
			{
				string msg = "Ошибка: не удалось извлечь infohash из magnet-ссылки";
				Log(msg);
				return;
			}

			string msgStart = $"Начало анализа треков для инфохаша: {infohash}";
			Log(msgStart);
			
			FfprobeModel res = null;
			string tsuri = AppInit.conf.tsuri[random.Next(0, AppInit.conf.tsuri.Length)];
			//string msgTsuri = $"Используется tsuri сервер: {tsuri}";
			//Log(msgTsuri);

			// Флаг для отслеживания, нужно ли делать очистку
			bool cleanupRequired = true;
			
			#region ffprobe
			using (var cancellationTokenSource = new CancellationTokenSource())
			{
				cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(3));
				var token = cancellationTokenSource.Token;

				System.Diagnostics.Process process = null;
				
				try
				{
					string media = $"{tsuri}/stream/file?link={HttpUtility.UrlEncode(magnet)}&index=1&play";
					string msgFfprobe = $"Запуск ffprobe для: {infohash}";
					Log(msgFfprobe);
					
					process = new System.Diagnostics.Process();
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.RedirectStandardError = true;
					process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
					process.StartInfo.FileName = "ffprobe";
					process.StartInfo.Arguments = $"-v quiet -print_format json -show_format -show_streams \"{media}\"";
					
					var outputBuilder = new StringBuilder();
					var errorBuilder = new StringBuilder();
					
					process.OutputDataReceived += (sender, e) => {
						if (!string.IsNullOrEmpty(e.Data))
							outputBuilder.AppendLine(e.Data);
					};
					
					process.ErrorDataReceived += (sender, e) => {
						if (!string.IsNullOrEmpty(e.Data))
							errorBuilder.AppendLine(e.Data);
					};
					
					if (!process.Start())
					{
						string msg = "Ошибка: не удалось запустить процесс ffprobe";
						Log(msg);
						return;
					}
					
					string msgPid = $"Процесс ffprobe запущен (PID: {process.Id})";
					Log(msgPid);
					
					process.BeginOutputReadLine();
					process.BeginErrorReadLine();
					
					// Создаем задачу завершения процесса
					var processExitTask = process.WaitForExitAsync(token);
					
					// Ожидаем завершение процесса или таймаут
					await processExitTask;
					
					// Если процесс еще не завершился, убиваем его
					if (!process.HasExited)
					{
						string msg = $"Таймаут анализа. Завершение процесса ffprobe (PID: {process.Id})...";
						Log(msg);
						try
						{
							KillProcessTree(process);
							string msgKilled = "Процесс ffprobe завершен принудительно";
							Log(msgKilled);
						}
						catch (Exception killEx)
						{
							string msgError = $"Ошибка при завершении процесса ffprobe: {killEx.Message}";
							Log(msgError);
						}
						// Продолжаем выполнение для очистки
					}
					else if (token.IsCancellationRequested)
					{
						string msg = "Операция анализа отменена";
						Log(msg);
						// Продолжаем выполнение для очистки
					}
					else if (process.ExitCode != 0)
					{
						string errorOutput = errorBuilder.ToString();
						string msgExitCode = $"ffprobe завершился с ошибкой (код: {process.ExitCode})";
						Log(msgExitCode);
						
						if (!string.IsNullOrEmpty(errorOutput))
						{
							// Для вывода ошибок ffprobe тоже используем формат tracks:
							string timeNow = DateTime.Now.ToString("HH:mm:ss");
							string msgError = $"tracks: [{timeNow}] Вывод ошибки: {errorOutput}";
							Console.WriteLine(msgError);
							
							// Обрезаем длинные сообщения об ошибках для лога
							if (errorOutput.Length > 500)
								errorOutput = errorOutput.Substring(0, 500) + "...";
							Log($"Вывод ошибки ffprobe: {errorOutput}");
						}
						// Продолжаем выполнение для очистки
					}
					else
					{
						string msgSuccess = "ffprobe успешно завершился";
						Log(msgSuccess);
						
						string outPut = outputBuilder.ToString();
						if (!string.IsNullOrEmpty(outPut))
						{
							res = JsonConvert.DeserializeObject<FfprobeModel>(outPut);
							string msgStreams = $"Получено {res?.streams?.Count ?? 0} треков";
							Log(msgStreams);
						}
						else
						{
							string msg = "Предупреждение: ffprobe не вернул данных";
							Log(msg);
						}
					}
				}
				catch (OperationCanceledException)
				{
					string msg = "Анализ треков отменен по таймауту (3 минуты)";
					Log(msg);
				}
				catch (Exception ex)
				{
					string msg = $"Критическая ошибка при анализе треков: {ex.Message}";
					Log(msg);
					
					// StackTrace также с форматом tracks:
					string timeNow = DateTime.Now.ToString("HH:mm:ss");
					string stackTraceMsg = $"tracks: [{timeNow}] StackTrace: {ex.StackTrace}";
					Console.WriteLine(stackTraceMsg);
					LogToFile($"StackTrace: {ex.StackTrace}");
				}
				finally
				{
					// Всегда освобождаем ресурсы процесса
					if (process != null)
					{
						// Дополнительная проверка, что процесс мертв
						if (!process.HasExited)
						{
							try
							{
								string msg = $"Принудительное завершение процесса в finally (PID: {process.Id})...";
								Log(msg);
								KillProcessTree(process);
							}
							catch { }
						}
						
						process.Dispose();
					}
					
					cancellationTokenSource.Dispose();
				}
			}
			#endregion

			// Очистка на сервере - ВСЕГДА после завершения ffprobe
			try
			{
				if (cleanupRequired)
				{
					string msgClean = "Очистка торрента на сервере...";
					Log(msgClean);
					
					await HttpClient.Post($"{tsuri}/torrents", 
						"{\"action\":\"wipe\",\"hash\":\"" + infohash + "\"}");
					string msgSuccess = $"Торрент {infohash} успешно удален с сервера";
					Log(msgSuccess);
				}
			}
			catch (Exception ex)
			{
				string msg = $"Ошибка при очистке торрента на сервере: {ex.Message}";
				Log(msg);
				// Не прерываем выполнение, продолжаем сохранение данных
			}

			// Если нет данных о треках, завершаем
			if (res?.streams == null || res.streams.Count == 0)
			{
				string msg = $"Нет данных о треках для инфохаша {infohash}. Завершение.";
				Log(msg);
				return;
			}

			// Сохранение данных треков
			int audioCount = res.streams.Count(s => s.codec_type == "audio");
			int videoCount = res.streams.Count(s => s.codec_type == "video");
			string msgStats = $"Сохранение данных треков в базу. Найдено аудио дорожек: {audioCount}, видео: {videoCount}";
			Log(msgStats);

			try
			{
				Database.AddOrUpdate(infohash, res, (k, v) => res);
				string msg = $"Данные треков для {infohash} обновлены в памяти";
				Log(msg);
			}
			catch (Exception ex)
			{
				string msg = $"Ошибка при обновлении данных в памяти: {ex.Message}";
				Log(msg);
			}

			try
			{
				string path = pathDb(infohash, createfolder: true);
				await File.WriteAllTextAsync(path, 
					JsonConvert.SerializeObject(res, Formatting.Indented));
				string msg = $"Данные треков сохранены в файл: {path}";
				Log(msg);
				
				// Дополнительная информация о сохраненных языках
				var audioLanguages = res.streams
					.Where(s => s.codec_type == "audio" && s.tags?.language != null)
					.Select(s => s.tags.language)
					.Distinct()
					.ToList();
					
				if (audioLanguages.Any())
				{
					string msgLang = $"Обнаружены аудио дорожки на языках: {string.Join(", ", audioLanguages)}";
					Log(msgLang);
				}
			}
			catch (Exception ex)
			{
				string msg = $"Ошибка при сохранении данных в файл: {ex.Message}";
				Log(msg);
				
				// StackTrace также с форматом tracks:
				string timeNow = DateTime.Now.ToString("HH:mm:ss");
				string stackTraceMsg = $"tracks: [{timeNow}] StackTrace: {ex.StackTrace}";
				Console.WriteLine(stackTraceMsg);
				LogToFile($"StackTrace: {ex.StackTrace}");
			}

			string msgFinal = $"Анализ треков для {infohash} успешно завершен!";
			Log(msgFinal);
		}

		// Метод для корректного убийства процесса и его дочерних процессов
		private static void KillProcessTree(System.Diagnostics.Process process)
		{
			if (process == null || process.HasExited)
				return;

			int processId = process.Id;
			
			try
			{
				// Сначала пробуем штатное завершение
				process.CloseMainWindow();
				
				// Ждем немного
				if (process.WaitForExit(500))
					return;
					
				// Если не вышло, пробуем Kill с entireProcessTree
				try
				{
					// Для .NET Core 3.0+ и .NET 5+
					process.Kill(entireProcessTree: true);
				}
				catch
				{
					// Для старых версий или если не поддерживается
					process.Kill();
					
					// На Linux пытаемся убить дерево процессов через pkill
					if (Environment.OSVersion.Platform == PlatformID.Unix || 
						Environment.OSVersion.Platform == PlatformID.MacOSX)
					{
						try
						{
							using var pkillProcess = new System.Diagnostics.Process();
							pkillProcess.StartInfo.FileName = "pkill";
							pkillProcess.StartInfo.Arguments = $"-9 -P {processId}";
							pkillProcess.StartInfo.UseShellExecute = false;
							pkillProcess.StartInfo.CreateNoWindow = true;
							pkillProcess.Start();
							pkillProcess.WaitForExit(1000);
						}
						catch { }
					}
				}
				
				// Ждем завершения
				if (!process.WaitForExit(2000))
				{
					// Последняя попытка - если процесс все еще жив
					try { process.Kill(); } catch { }
				}
			}
			catch (InvalidOperationException)
			{
				// Процесс уже завершился
			}
			catch (Exception ex)
			{
				// Логируем ошибку убийства процесса с форматом tracks:
				try
				{
					string timeNow = DateTime.Now.ToString("HH:mm:ss");
					string errorMsg = $"Ошибка в KillProcessTree для PID {processId}: {ex.Message}";
					string logMessage = $"tracks: [{timeNow}] {errorMsg}";
					Console.WriteLine(logMessage);
					
					if (AppInit.conf != null)
					{
						var prop = AppInit.conf.GetType().GetProperty("trackslog");
						if (prop != null && prop.PropertyType == typeof(bool))
						{
							var value = prop.GetValue(AppInit.conf);
							if (value != null && (bool)value)
							{
								string logDir = "Data/log";  // Изменено с Data/temp на Data/log
								string logFile = Path.Combine(logDir, "tracks.log");
								
								if (!Directory.Exists(logDir))
									Directory.CreateDirectory(logDir);
								
								File.AppendAllText(logFile, $"{logMessage}{Environment.NewLine}", Encoding.UTF8);
							}
						}
					}
				}
				catch { }
			}
		}
///

        public static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
        {
            try
            {
                var languages = new HashSet<string>();

                if (t.languages != null)
                {
                    foreach (var l in t.languages)
                        languages.Add(l);
                }

                if (streams != null)
                {
                    foreach (var item in streams)
                    {
                        if (!string.IsNullOrEmpty(item.tags?.language) && item.codec_type == "audio")
                            languages.Add(item.tags.language);
                    }
                }

                if (languages.Count == 0)
                    return null;

                return languages;
            }
            catch { return null; }
        }
    }
}

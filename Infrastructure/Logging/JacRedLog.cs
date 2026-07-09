using Microsoft.Extensions.Logging;
using System;

namespace JacRed.Infrastructure.Logging
{
    public static class JacRedLog
    {
        static ILoggerFactory _factory;

        public static void Configure(ILoggerFactory factory)
        {
            _factory = factory;
        }

        public static void Debug(string category, string message) => Write(category, LogLevel.Debug, message);
        public static void Information(string category, string message) => Write(category, LogLevel.Information, message);
        public static void Warning(string category, string message) => Write(category, LogLevel.Warning, message);
        public static void Error(string category, string message) => Write(category, LogLevel.Error, message);

        public static void Write(string category, LogLevel level, string message)
        {
            if (!JacRedLogSettings.IsEnabled(category, level))
                return;

            var line = FormatLine(category, message);
            if (_factory == null)
            {
                Console.WriteLine(line);
                return;
            }

            _factory.CreateLogger("JacRed." + category).Log(level, "{Line}", line);
        }

        static string FormatLine(string category, string message)
        {
            if (JacRedLogSettings.ConsoleTimestamp && !message.StartsWith("[", StringComparison.Ordinal))
                return $"{category}: [{DateTime.Now:HH:mm:ss}] {message}";
            return $"{category}: {message}";
        }

        /// <summary>Classify tracks pipeline messages when level not specified.</summary>
        public static LogLevel ClassifyTracksMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return LogLevel.Information;
            if (message.Contains("без результата", StringComparison.Ordinal)
                || message.Contains("Нет данных", StringComparison.Ordinal)
                || message.Contains("Ошибка", StringComparison.Ordinal)
                || message.Contains("не удалось", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Warning;
            if (message.Contains("Начало анализа", StringComparison.Ordinal)
                || message.Contains("успешно добавлен", StringComparison.Ordinal)
                || message.Contains("успешно удален", StringComparison.Ordinal)
                || message.Contains("не требуется", StringComparison.Ordinal)
                || message.Contains("уже существует", StringComparison.Ordinal)
                || message.Contains("API успешно", StringComparison.Ordinal)
                || message.Contains("Сохранение данных", StringComparison.Ordinal)
                || message.Contains("Обнаружены аудио", StringComparison.Ordinal)
                || message.Contains("Пауза", StringComparison.Ordinal))
                return LogLevel.Debug;
            if (message.Contains("успешно завершен", StringComparison.Ordinal))
                return LogLevel.Information;
            return LogLevel.Information;
        }
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.IO;

namespace JacRed.Infrastructure.Logging
{
    /// <summary>Writes log message as-is (JacRedLog embeds category prefix in the line).</summary>
    public sealed class JacRedConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "jacred";

        public JacRedConsoleFormatter() : base(FormatterName) { }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (string.IsNullOrEmpty(message)) return;
            textWriter.WriteLine(message);
        }
    }

    public sealed class JacRedConsoleFormatterOptions : ConsoleFormatterOptions { }

    public sealed class JacRedConsoleFormatterConfigureOptions : IConfigureOptions<ConsoleLoggerOptions>
    {
        public void Configure(ConsoleLoggerOptions options)
        {
            options.FormatterName = JacRedConsoleFormatter.FormatterName;
        }
    }
}

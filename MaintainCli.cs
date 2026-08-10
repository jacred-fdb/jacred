using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using JacRed.Application.Index;
using JacRed.Application.Maintenance;
using JacRed.Infrastructure.Logging;

namespace JacRed
{
    /// <summary>Headless FDB integrity CLI: <c>JacRed maintain [--mode=report|safe|full] …</c></summary>
    static class MaintainCli
    {
        public static int Run(string[] args)
        {
            PrintBanner();

            if (!TryParseArgs(args, out string mode, out int sampleSize, out bool excludeNumericXx, out bool showHelp, out string error))
            {
                if (showHelp)
                {
                    PrintUsage();
                    return 0;
                }
                Console.Error.WriteLine(error);
                PrintUsage();
                return 2;
            }

            Directory.CreateDirectory("Data/fdb");
            Directory.CreateDirectory("Data/temp");
            Directory.CreateDirectory("Data/log");
            Directory.CreateDirectory("Data/tracks");

            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Touch config so FileDB path layout (fdbPathLevels) is correct.
            _ = AppInit.conf;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine();
                Console.WriteLine("[maintain] Ctrl+C — cancelling…");
                cts.Cancel();
            };

            Console.WriteLine($"[maintain] cwd={Directory.GetCurrentDirectory()}");
            Console.WriteLine($"[maintain] mode={mode} sampleSize={sampleSize} excludeNumericXx={excludeNumericXx}");
            Console.WriteLine("[maintain] Tip: stop JacRed before safe/full so only this process touches Data/.");
            Console.WriteLine();

            var service = new FdbMaintenanceService(FastDbIndex.Default);
            try
            {
                bool ok = service.Run(mode, sampleSize, excludeNumericXx, cts.Token, consoleProgress: true);
                return ok ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                JacRedLog.Warning(JacRedLogCategories.Fdb, "maintain CLI cancelled");
                return 130;
            }
        }

        static void PrintBanner()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  JacRed maintain — offline FDB integrity");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"  Version:     {VersionInfo.Version}");
            Console.WriteLine($"  Git SHA:     {VersionInfo.GitSha}");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
        }

        static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  JacRed maintain [--mode=report|safe|full] [--sample-size=20] [--include-numeric-xx]");
            Console.WriteLine();
            Console.WriteLine("  Run from the install directory (where Data/ lives), e.g. /opt/jacred.");
            Console.WriteLine("  Default mode=report (read-only). Report: Data/temp/maintenance-last.json");
        }

        /// <summary>Parse args after the leading "maintain" verb.</summary>
        internal static bool TryParseArgs(string[] args, out string mode, out int sampleSize,
            out bool excludeNumericXx, out bool showHelp, out string error)
        {
            mode = "report";
            sampleSize = 20;
            excludeNumericXx = true;
            showHelp = false;
            error = null;

            // args[0] is "maintain"; options follow
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    showHelp = true;
                    return false;
                }

                if (a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
                {
                    mode = a.Substring("--mode=".Length).Trim();
                    if (mode is not ("report" or "safe" or "full"))
                    {
                        error = $"Unknown mode '{mode}'. Use report, safe, or full.";
                        return false;
                    }
                    continue;
                }

                if (a.StartsWith("--sample-size=", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(a.Substring("--sample-size=".Length), out sampleSize) || sampleSize < 1)
                    {
                        error = "Invalid --sample-size (positive integer expected).";
                        return false;
                    }
                    continue;
                }

                if (a.Equals("--include-numeric-xx", StringComparison.OrdinalIgnoreCase))
                {
                    excludeNumericXx = false;
                    continue;
                }

                error = $"Unknown argument: {a}";
                return false;
            }

            return true;
        }
    }
}

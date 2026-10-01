using System;
using System.IO;

namespace Harness
{
    /// <summary>
    /// Paths the harness needs. CI sets them; locally, set them by hand.
    ///   HARNESS_BOT_DIR        the legacy bot build output (contains SpixiBot.dll), .NET 8
    ///   HARNESS_SIMCLIENT_DLL  SimClient.dll built against the client Core, .NET 10
    ///   HARNESS_ARTIFACTS      optional; per-test logs are copied here (CI uploads it)
    ///   HARNESS_BOT_CONSOLE    optional; 1 = bot in its own console window, events from ixian.log (Windows)
    /// </summary>
    internal static class HarnessConfig
    {
        public static string BotDir => Required("HARNESS_BOT_DIR", "SpixiBot.dll");
        public static string SimClientDll => Required("HARNESS_SIMCLIENT_DLL", null);
        public static string? ArtifactsDir => Environment.GetEnvironmentVariable("HARNESS_ARTIFACTS");

        /// <summary>
        /// HARNESS_BOT_CONSOLE=1 (Windows CI): start the bot in its own console window and read its log file
        /// instead of stdout. The unpatched bot calls Console.Clear() first (Program.cs:142), which throws
        /// IOException on a redirected stdout on Windows (CI run 36840465376).
        /// </summary>
        public static bool BotOwnConsole => Environment.GetEnvironmentVariable("HARNESS_BOT_CONSOLE") == "1";

        /// <summary>
        /// W2: every wait is "until event or timeout"; the timeout is at least 3× the larger reconnect interval
        /// (Core 2 s, app loop 2.5 s → 7.5 s). Start-up waits also cover RSA keygen (no wallet pool).
        /// </summary>
        public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(20);

        private static string Required(string name, string? mustContain)
        {
            string? v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v))
            {
                throw new InvalidOperationException($"Environment variable {name} is not set (see tests/Harness/HarnessConfig.cs).");
            }
            v = Path.GetFullPath(v);
            if (mustContain != null && !File.Exists(Path.Combine(v, mustContain)))
            {
                throw new InvalidOperationException($"{name}={v} does not contain {mustContain}.");
            }
            if (mustContain == null && !File.Exists(v))
            {
                throw new InvalidOperationException($"{name}={v} does not exist.");
            }
            return v;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace SimClient
{
    /// <summary>
    /// The only writer to stdout. One JSON object per line: {"ev":"...","t":ms,...}.
    /// Core logging goes to a file (Logging.consoleOutput = false), so stdout stays parseable. Any line that
    /// is not a JSON object (for example a Core Console.WriteLine) is treated as log text by the harness.
    /// </summary>
    internal static class Events
    {
        private static readonly object writeLock = new object();
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        public static void Emit(string ev, Dictionary<string, object?>? fields = null)
        {
            var obj = new Dictionary<string, object?> { ["ev"] = ev, ["t"] = clock.ElapsedMilliseconds };
            if (fields != null)
            {
                foreach (var kv in fields)
                {
                    obj[kv.Key] = kv.Value;
                }
            }
            string line = JsonSerializer.Serialize(obj);
            lock (writeLock)
            {
                Console.Out.WriteLine(line);
                Console.Out.Flush();
            }
        }

        public static void Error(string where, Exception e)
        {
            Emit("error", new Dictionary<string, object?> { ["where"] = where, ["error"] = e.GetType().Name + ": " + e.Message });
        }
    }
}

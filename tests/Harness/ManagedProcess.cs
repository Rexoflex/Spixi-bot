using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Harness
{
    /// <summary>One line of process output. Event = a JSON object with an "ev" field; anything else is log text.</summary>
    internal sealed class OutputLine
    {
        public required DateTime At { get; init; }
        public required string Stream { get; init; }   // stdout | stderr | harness
        public required string Text { get; init; }
        public JsonElement? Event { get; init; }

        public string? Ev => Event is JsonElement e && e.TryGetProperty("ev", out var v) ? v.GetString() : null;

        public string? Str(string name) =>
            Event is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public int? Int(string name) =>
            Event is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    /// <summary>
    /// A child process with redirected stdio. Every output line is kept; waits are "until a matching line or
    /// timeout" (W2, no sleeps). A process exit becomes a synthetic "exited" event (W12), so a test can wait
    /// for it or fail fast on it.
    /// </summary>
    internal sealed class ManagedProcess : IAsyncDisposable
    {
        private readonly Process process;
        private readonly List<OutputLine> lines = new List<OutputLine>();
        private readonly object sync = new object();
        private TaskCompletionSource changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int streamsOpen = 2;

        public string Name { get; }
        public bool HasExited => process.HasExited;

        private readonly string? tailFile;
        private readonly CancellationTokenSource tailStop = new CancellationTokenSource();

        /// <summary>
        /// Own-console mode (Windows bot): no redirect, so the child gets a real console; output lines come from
        /// tailing <paramref name="tailFile"/> (the bot's ixian.log, written by Core Logging).
        /// </summary>
        public static ManagedProcess StartWithOwnConsole(string name, string fileName, IEnumerable<string> args, string workingDirectory, string tailFile)
        {
            return new ManagedProcess(name, fileName, args, workingDirectory, tailFile);
        }

        private ManagedProcess(string name, string fileName, IEnumerable<string> args, string workingDirectory, string tailFile)
        {
            Name = name;
            this.tailFile = tailFile;
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Minimized,
                Arguments = string.Join(" ", RequireNoSpaces(args)),   // shell-execute takes one string (review R1 n1)
            };
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (s, e) =>
            {
                int code = -1;
                try { code = process.ExitCode; } catch (Exception) { }
                string json = $"{{\"ev\":\"exited\",\"code\":{code}}}";
                Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = json, Event = JsonDocument.Parse(json).RootElement.Clone() });
            };
            process.Start();
            Task.Run(() => TailLoop(tailStop.Token));
        }

        private static IEnumerable<string> RequireNoSpaces(IEnumerable<string> args)
        {
            foreach (string a in args)
            {
                if (a.Length == 0 || a.Any(c => char.IsWhiteSpace(c) || c == '"'))
                {
                    throw new ArgumentException("own-console mode cannot pass an empty argument or one with spaces or quotes: '" + a + "'");
                }
            }
            return args;
        }

        /// <summary>Reads new lines from the tail file as they are written (FileShare.ReadWrite: the bot keeps it open).</summary>
        private async Task TailLoop(CancellationToken ct)
        {
            long pos = 0;
            string pending = "";
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(tailFile))
                    {
                        using var fs = new FileStream(tailFile!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (fs.Length < pos) { pos = 0; }      // rotated
                        fs.Seek(pos, SeekOrigin.Begin);
                        using var sr = new StreamReader(fs, Encoding.UTF8);
                        string chunk = await sr.ReadToEndAsync().ConfigureAwait(false);
                        pos = fs.Position;
                        pending += chunk;
                        int nl;
                        while ((nl = pending.IndexOf('\n')) >= 0)
                        {
                            string line = pending.Substring(0, nl).TrimEnd('\r');
                            pending = pending.Substring(nl + 1);
                            Add(new OutputLine { At = DateTime.UtcNow, Stream = "log", Text = line });
                        }
                    }
                }
                catch (IOException) { }
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }

        public ManagedProcess(string name, string fileName, IEnumerable<string> args, string workingDirectory)
        {
            Name = name;
            var psi = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => OnLine("stdout", e.Data);
            process.ErrorDataReceived += (s, e) => OnLine("stderr", e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private void OnLine(string stream, string? text)
        {
            if (text == null)
            {
                // End of one stream. After both close, the process is gone: record it once (W12).
                if (Interlocked.Decrement(ref streamsOpen) == 0)
                {
                    process.WaitForExit(5000);
                    int code = process.HasExited ? process.ExitCode : -1;
                    Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = $"{{\"ev\":\"exited\",\"code\":{code}}}",
                        Event = JsonDocument.Parse($"{{\"ev\":\"exited\",\"code\":{code}}}").RootElement.Clone() });
                }
                return;
            }
            JsonElement? ev = null;
            string t = text.TrimStart();
            if (t.StartsWith("{\"ev\":", StringComparison.Ordinal))
            {
                try { ev = JsonDocument.Parse(t).RootElement.Clone(); } catch (JsonException) { }
            }
            Add(new OutputLine { At = DateTime.UtcNow, Stream = stream, Text = text, Event = ev });
        }

        private void Add(OutputLine l)
        {
            TaskCompletionSource old;
            lock (sync)
            {
                lines.Add(l);
                old = changed;
                changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            old.TrySetResult();
        }

        public List<OutputLine> Snapshot()
        {
            lock (sync)
            {
                return lines.ToList();
            }
        }

        /// <summary>Events that end a wait at once (review R1 n6): the process reported it cannot go on.</summary>
        private static bool IsFatal(OutputLine l) => l.Ev is "fatal" or "crashed";

        /// <summary>Waits until a line (from index 0) matches. Throws HarnessTimeoutException with `what`.</summary>

        public async Task<OutputLine> WaitFor(Func<OutputLine, bool> match, TimeSpan timeout, string what, bool failOnExit = true, Func<OutputLine, bool>? failIf = null)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Task next;
                lock (sync)
                {
                    foreach (OutputLine l in lines)
                    {
                        if (match(l))
                        {
                            return l;
                        }
                        if (failOnExit && l.Ev == "exited")
                        {
                            throw new HarnessTimeoutException($"{Name}: process exited while waiting for {what}");
                        }
                        if (failIf != null && failIf(l))
                        {
                            throw new HarnessTimeoutException($"{Name}: '{l.Ev}' while waiting for {what}: {l.Text}");
                        }
                        if (failOnExit && IsFatal(l))
                        {
                            throw new HarnessTimeoutException($"{Name}: '{l.Ev}' while waiting for {what}: {l.Text}");
                        }
                    }
                    next = changed.Task;
                }
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    throw new HarnessTimeoutException($"{Name}: no {what} within {timeout.TotalSeconds:0} s");
                }
                await Task.WhenAny(next, Task.Delay(left)).ConfigureAwait(false);
            }
        }

        public Task<OutputLine> WaitForEvent(string ev, TimeSpan timeout, Func<OutputLine, bool>? and = null) =>
            WaitFor(l => l.Ev == ev && (and == null || and(l)), timeout, $"'{ev}' event");

        public void Send(object command)
        {
            string json = JsonSerializer.Serialize(command);
            Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = "> " + json });
            process.StandardInput.WriteLine(json);
            process.StandardInput.Flush();
        }

        public string Dump()
        {
            var sb = new StringBuilder();
            string state;
            try { state = process.HasExited ? "exited " + process.ExitCode : "running"; } catch (InvalidOperationException) { state = "disposed"; }
            sb.AppendLine($"===== {Name} ({state}) =====");
            foreach (OutputLine l in Snapshot())
            {
                sb.Append(l.At.ToString("HH:mm:ss.fff")).Append(' ').Append(l.Stream).Append(' ').AppendLine(l.Text);
            }
            return sb.ToString();
        }

        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }
            tailStop.Cancel();
            if (tailFile != null)
            {
                // Own-console mode has no stdin to close: stop the process directly.
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); } } catch (Exception) { }
                process.Dispose();
                return;
            }
            try
            {
                if (!process.HasExited)
                {
                    try { process.StandardInput.Close(); } catch (Exception) { }
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }
                }
            }
            catch (Exception) { }
            process.Dispose();
        }
    }

    internal sealed class HarnessTimeoutException : Exception
    {
        public HarnessTimeoutException(string message) : base(message) { }
    }
}

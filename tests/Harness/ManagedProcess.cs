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

        public long? Long(string name) =>
            Event is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

        /// <summary>The value of a JSON boolean field; null when the field is missing or not a boolean.</summary>
        public bool? Bool(string name) =>
            Event is JsonElement e && e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;

        /// <summary>The field is missing or JSON null.</summary>
        public bool IsNull(string name) =>
            !(Event is JsonElement e && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null);

        /// <summary>A hex field equals <paramref name="hex"/>, ignoring case (ids come from different encoders).</summary>
        public bool HexIs(string name, string? hex) =>
            hex != null && string.Equals(Str(name), hex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A child process with redirected stdio. Every output line is kept; waits are "until a matching line or
    /// timeout" (W2, no sleeps). A process exit becomes a synthetic "exited" event (W12), so a test can wait
    /// for it or fail fast on it. An exit without the process's own `bye` and with a non-zero code that the harness
    /// did not cause is followed by a synthetic "crashed" event (W12, see <see cref="AddExit"/>).
    /// </summary>
    internal sealed class ManagedProcess : IAsyncDisposable
    {
        private readonly Process process;
        private readonly List<OutputLine> lines = new List<OutputLine>();
        private readonly object sync = new object();
        private TaskCompletionSource changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int streamsOpen = 2;

        /// <summary>Windows STATUS_STACK_OVERFLOW (0xC00000FD) as a .NET exit code.</summary>
        private const int StackOverflowExitCode = unchecked((int)0xC00000FD);

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
                AddExit(code);
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
                    AddExit(code);
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

        private int Add(OutputLine l)
        {
            TaskCompletionSource old;
            int index;
            lock (sync)
            {
                index = lines.Count;
                lines.Add(l);
                old = changed;
                changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            old.TrySetResult();
            return index;
        }

        /// <summary>
        /// W12: records the exit as an "exited" event. When the process ended without printing its own `bye`, with a
        /// non-zero code, and not because the harness stopped it (dispose), a synthetic "crashed" event follows:
        /// {code, stackOverflow, source:"exit"}. stackOverflow = the Windows STATUS_STACK_OVERFLOW code, or the
        /// runtime's "Stack overflow" line on stderr (redirect mode: stderr is read line by line, OnLine, and both
        /// streams are closed before this runs). A stack overflow or Environment.FailFast cannot be caught, so the
        /// process cannot report it itself. Both lines are added under one lock: no waiter sees `exited` without
        /// its `crashed`.
        /// </summary>
        private void AddExit(int code)
        {
            string exitedJson = $"{{\"ev\":\"exited\",\"code\":{code}}}";
            var add = new List<OutputLine>
            {
                new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = exitedJson, Event = JsonDocument.Parse(exitedJson).RootElement.Clone() },
            };
            bool sawBye;
            bool stackOverflowText;
            lock (sync)
            {
                sawBye = lines.Any(l => l.Ev == "bye");
                stackOverflowText = lines.Any(l => l.Stream == "stderr" && l.Text.Contains("Stack overflow", StringComparison.Ordinal));
            }
            if (!sawBye && code != 0 && Volatile.Read(ref disposed) == 0)
            {
                bool stackOverflow = code == StackOverflowExitCode || stackOverflowText;
                string crashedJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["ev"] = "crashed", ["code"] = code, ["stackOverflow"] = stackOverflow, ["source"] = "exit",
                });
                add.Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = crashedJson, Event = JsonDocument.Parse(crashedJson).RootElement.Clone() });
            }
            TaskCompletionSource old;
            lock (sync)
            {
                lines.AddRange(add);
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

        /// <summary>
        /// Waits until a line at or after <paramref name="from"/> matches. Throws HarnessTimeoutException with `what`.
        /// A <paramref name="failIf"/> line (at or after from) before the first match ends the wait. An exit, fatal or
        /// crash anywhere in the log ends the wait when no line matches (the process cannot go on). A line that
        /// matches wins over an exit: it was written, and a wait FOR the synthetic `crashed` (which follows `exited`,
        /// W12) must succeed.
        /// </summary>
        public async Task<OutputLine> WaitFor(Func<OutputLine, bool> match, TimeSpan timeout, string what, bool failOnExit = true, Func<OutputLine, bool>? failIf = null, int from = 0)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Task next;
                lock (sync)
                {
                    int hit = -1;
                    for (int i = Math.Max(0, from); i < lines.Count; i++)
                    {
                        if (match(lines[i]))
                        {
                            hit = i;
                            break;
                        }
                    }
                    if (failIf != null)
                    {
                        int end = hit >= 0 ? hit : lines.Count;
                        for (int i = Math.Max(0, from); i < end; i++)
                        {
                            if (failIf(lines[i]))
                            {
                                throw new HarnessTimeoutException($"{Name}: '{lines[i].Ev}' while waiting for {what}: {lines[i].Text}");
                            }
                        }
                    }
                    if (hit >= 0)
                    {
                        return lines[hit];
                    }
                    if (failOnExit)
                    {
                        foreach (OutputLine l in lines)
                        {
                            if (l.Ev == "exited")
                            {
                                throw new HarnessTimeoutException($"{Name}: process exited while waiting for {what}");
                            }
                            if (IsFatal(l))
                            {
                                throw new HarnessTimeoutException($"{Name}: '{l.Ev}' while waiting for {what}: {l.Text}");
                            }
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

        public Task<OutputLine> WaitForEvent(string ev, TimeSpan timeout, Func<OutputLine, bool>? and = null, int from = 0) =>
            WaitFor(l => l.Ev == ev && (and == null || and(l)), timeout, $"'{ev}' event", from: from);

        /// <summary>A harness note in the event log (for the report only; never an event).</summary>
        public void Note(string text) => Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = "# " + text });

        /// <summary>
        /// Sends one command line. Returns its position in the event log: every line the process writes because of
        /// this command comes later (use it as `from` in <see cref="WaitFor"/> and <see cref="Since"/>).
        /// </summary>
        public int Send(object command)
        {
            string json = JsonSerializer.Serialize(command);
            int mark = Add(new OutputLine { At = DateTime.UtcNow, Stream = "harness", Text = "> " + json });
            process.StandardInput.WriteLine(json);
            process.StandardInput.Flush();
            return mark;
        }

        /// <summary>The current end of the event log (a mark for "from now on").</summary>
        public int Mark()
        {
            lock (sync)
            {
                return lines.Count;
            }
        }

        /// <summary>Lines at or after <paramref name="from"/>, in arrival order.</summary>
        public List<OutputLine> Since(int from)
        {
            lock (sync)
            {
                return lines.Skip(from).ToList();
            }
        }

        /// <summary>Position of <paramref name="line"/> (a line of this log, by reference) in the event log, or -1.</summary>
        public int IndexOf(OutputLine line)
        {
            lock (sync)
            {
                return lines.IndexOf(line);
            }
        }

        /// <summary>
        /// Lines at or after <paramref name="from"/> and before <paramref name="end"/> (a line of this log), in arrival
        /// order. With end = a sync event (e.g. `injected`), this is everything the step caused.
        /// </summary>
        public List<OutputLine> Between(int from, OutputLine end)
        {
            lock (sync)
            {
                int stop = lines.IndexOf(end);
                if (stop < 0)
                {
                    throw new InvalidOperationException("Between: the end line is not in this log");
                }
                return lines.Skip(from).Take(Math.Max(0, stop - from)).ToList();
            }
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

    internal class HarnessTimeoutException : Exception
    {
        public HarnessTimeoutException(string message) : base(message) { }
    }

    /// <summary>D-044: the environment failed (testnet unreachable), not the bot. CI labels it INFRA.</summary>
    internal sealed class HarnessInfraException : Exception
    {
        public HarnessInfraException(string message) : base(message) { }
    }
}

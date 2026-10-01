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

        /// <summary>Waits until a line (from index 0) matches. Throws HarnessTimeoutException with `what`.</summary>
        public async Task<OutputLine> WaitFor(Func<OutputLine, bool> match, TimeSpan timeout, string what, bool failOnExit = true)
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

        public async ValueTask DisposeAsync()
        {
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

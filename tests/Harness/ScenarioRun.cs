using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Harness
{
    /// <summary>
    /// One scenario run (harness.md W2, W11): a fresh run folder, one bot with seeded defaults, any number of
    /// members, and a full event report on success and on failure. Every started process is registered for the
    /// report before any wait, so a start-up crash is visible (CI run 36841419911).
    ///
    /// Usage: <c>await using var run = await ScenarioRun.StartAsync(output, "join:store");</c>, then
    /// <see cref="StartMemberAsync"/>, then <see cref="Pass"/> as the last line of the test. A run disposed without
    /// <see cref="Pass"/> is reported as failed.
    /// </summary>
    internal sealed class ScenarioRun : IAsyncDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly List<ManagedProcess> processes = new List<ManagedProcess>();
        private readonly List<SimMember> members = new List<SimMember>();
        private bool passed;

        public string RunDir { get; }
        public string CaseLabel { get; }
        public BotProcess Bot { get; private set; } = null!;

        private ScenarioRun(ITestOutputHelper output, string caseLabel)
        {
            this.output = output;
            CaseLabel = caseLabel;
            RunDir = Path.Combine(Path.GetTempPath(), "spixibot-harness",
                Regex.Replace(caseLabel, @"[^A-Za-z0-9]+", "-") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RunDir);
        }

        /// <summary>Starts the bot (incl. the D-044 header guard) and seeds the default group and channel.</summary>
        public static async Task<ScenarioRun> StartAsync(ITestOutputHelper output, string caseLabel)
        {
            var run = new ScenarioRun(output, caseLabel);
            try
            {
                run.Bot = await BotProcess.StartAsync(run.RunDir, run.processes).ConfigureAwait(false);
                await run.Bot.WaitForHeaderAsync(caseLabel).ConfigureAwait(false);
                await run.Bot.SeedDefaultsAsync().ConfigureAwait(false);
                return run;
            }
            catch (Exception)
            {
                await run.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<SimMember> StartMemberAsync(string name, string app)
        {
            SimMember m = SimMember.Start(RunDir, name, app);
            members.Add(m);
            processes.Add(m.Process);
            await m.WaitReadyAsync().ConfigureAwait(false);
            return m;
        }

        public void Pass()
        {
            passed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (Bot != null && members.Exists(m => m.App == "store"))
            {
                output.WriteLine("NOTE W3: store mode runs on Core k until BE-04 names the store app's Core (hypothesis).");
            }
            SafeReport(!passed);
            // Members first (they get 'quit' and exit cleanly), then every started process, including a bot whose
            // start threw (review R1 m2). Dispose is idempotent.
            for (int i = members.Count - 1; i >= 0; i--)
            {
                await members[i].DisposeAsync().ConfigureAwait(false);
            }
            foreach (ManagedProcess p in processes)
            {
                await p.DisposeAsync().ConfigureAwait(false);
            }
            Bot?.DisposeHttp();
        }

        private void SafeReport(bool failed)
        {
            try
            {
                Report(failed);
            }
            catch (Exception e)
            {
                // A logging error must not hide the test result (review R1 n7).
                output.WriteLine("report failed: " + e.Message);
            }
        }

        /// <summary>W2: the full event log of every process goes to the test output and to HARNESS_ARTIFACTS.</summary>
        private void Report(bool failed)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"===== case {CaseLabel} ({(failed ? "FAILED" : "passed")}) =====");
            foreach (ManagedProcess p in processes)
            {
                text.Append(p.Dump());
            }
            if (Bot != null && failed)
            {
                text.Append(Bot.LogTail());
            }
            // The bot logs its config lines verbatim, including the API login (Config.cs:185). The password is a
            // random throwaway, but logs and annotations are public: redact it (review R1 m7, threat model S6).
            string all = Regex.Replace(text.ToString(), @"(addApiUser'?\s*=\s*'?)[^'\s]+", "$1<redacted>");
            output.WriteLine(all);
            string? art = HarnessConfig.ArtifactsDir;
            if (art != null)
            {
                Directory.CreateDirectory(art);
                File.WriteAllText(Path.Combine(art, Path.GetFileName(RunDir) + (failed ? ".FAILED" : "") + ".log"), all);
            }
        }
    }
}

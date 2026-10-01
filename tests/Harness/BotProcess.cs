using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Harness
{
    /// <summary>
    /// The legacy bot (a5a3442 code, .NET 8, Core f6fb55b) as a child process (W11).
    /// Each instance gets a fresh copy of the bot output folder: the bot looks for its DLLs in the working
    /// folder and writes Data/, activity/ and its log there (Program.cs:33-41, Config.cs:95, Node.cs:235).
    /// Started with: -t (testnet), the seed from HarnessConfig.BotSeed (default: Core's testnet seeds; F4 is
    /// refuted, the bot needs a block header before it answers hello), free -p/-a ports,
    /// -i 127.0.0.1, --disableWebStart, and --walletPassword (testnet only, Node.cs:120-126) so the bot makes
    /// its own wallet. The API login comes from ixian.cfg (addApiUser; no ':' in the password, Config.cs:207).
    /// </summary>
    internal sealed class BotProcess
    {
        public ManagedProcess Process { get; }
        public string WorkDir { get; }
        public int BotPort { get; }
        public int ApiPort { get; }
        public string Address { get; private set; } = "";
        public string Host => "127.0.0.1:" + BotPort;

        private readonly string apiUser = "harness";
        private readonly string apiPassword = Guid.NewGuid().ToString("N");
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private BotProcess(string workDir)
        {
            WorkDir = workDir;
            var (botPort, apiPort) = FreePortPair();
            BotPort = botPort;
            ApiPort = apiPort;
            File.WriteAllText(Path.Combine(workDir, "ixian.cfg"), $"addApiUser = {apiUser}:{apiPassword}\n");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiUser}:{apiPassword}")));
            var argList = new List<string> { "SpixiBot.dll", "-t" };
            if (HarnessConfig.BotSeed != "testnet")
            {
                argList.AddRange(new[] { "-n", "127.0.0.1:1" });
            }
            argList.AddRange(new[] { "-p", BotPort.ToString(), "-a", ApiPort.ToString(),
                "-i", "127.0.0.1", "--disableWebStart", "--walletPassword", "harness-bot-wallet-pw" });
            string[] args = argList.ToArray();
            Process = HarnessConfig.BotOwnConsole
                ? ManagedProcess.StartWithOwnConsole("bot", DotnetHost(), args, workDir, Path.Combine(workDir, "ixian.log"))
                : new ManagedProcess("bot", "dotnet", args, workDir);
        }

        /// <summary>Shell-execute does not search PATH the same way; use the SDK that setup-dotnet installed.</summary>
        private static string DotnetHost()
        {
            string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(root))
            {
                string exe = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
                if (File.Exists(exe))
                {
                    return exe;
                }
            }
            return "dotnet";
        }

        /// <summary>
        /// Starts the bot and registers its process in <paramref name="report"/> before any wait. Returns when the
        /// stream port is open. Call <see cref="WaitForHeaderAsync"/> next (ScenarioRun does): the bot serves
        /// clients only once it holds a block header (D-044).
        /// </summary>
        public static async Task<BotProcess> StartAsync(string runDir, List<ManagedProcess> report)
        {
            string work = Path.Combine(runDir, "bot");
            CopyDir(HarnessConfig.BotDir, work);
            var bot = new BotProcess(work);
            report.Add(bot.Process);

            // "Public Node Address: <base58>" is logged after the wallet loads (Node.cs:218). It reaches stdout
            // (verbose console during start-up) and ixian.log, so it works in both launch modes.
            var addrRe = new Regex("Public Node Address: ([1-9A-HJ-NP-Za-km-z]{40,})");
            OutputLine addrLine = await bot.Process.WaitFor(l => addrRe.IsMatch(StripAnsi(l.Text)),
                HarnessConfig.StartTimeout, "'Public Node Address' line").ConfigureAwait(false);
            bot.Address = addrRe.Match(StripAnsi(addrLine.Text)).Groups[1].Value;

            // Ready = the stream port accepts TCP. Poll by event-free probe: each probe is a real connect attempt,
            // bounded by the same start timeout (no fixed sleeps on the success path).
            DateTime deadline = DateTime.UtcNow + HarnessConfig.StartTimeout;
            while (!await CanConnect(bot.BotPort).ConfigureAwait(false))
            {
                if (bot.Process.HasExited || DateTime.UtcNow > deadline)
                {
                    throw new HarnessTimeoutException($"bot: stream port {bot.BotPort} not listening (exited={bot.Process.HasExited})");
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return bot;
        }

        /// <summary>
        /// D-044 infra guard. The old bot answers every client hello with "bye: not ready" until its TIV holds a
        /// block header (Core f6fb55b CoreNetworkProtocol.cs:509-514); the header comes from the public testnet
        /// seeds. The API method `blockheight` returns the TIV header height (GenericAPIServer.cs:1422-1429 →
        /// SpixiBot Node.cs:423-430, 0 while there is no header). If it stays 0 for
        /// <see cref="HarnessConfig.HeaderTimeout"/>, the cause is the network, not the bot: fail with
        /// <see cref="Markers.TestnetUnreachable"/>[case] before any member starts. The self-test variant
        /// `seed-none` proves this (L5): no seed → every case must fail with this marker.
        /// </summary>
        public async Task WaitForHeaderAsync(string caseLabel)
        {
            DateTime start = DateTime.UtcNow;
            DateTime deadline = start + HarnessConfig.HeaderTimeout;
            string last = "";
            bool sawZero = false;   // the API answered with a valid height 0: the bot runs, only the header is missing
            while (true)
            {
                if (Process.HasExited)
                {
                    throw new HarnessTimeoutException("bot exited while waiting for a block header");
                }
                try
                {
                    string body = await Api("blockheight").ConfigureAwait(false);
                    using JsonDocument doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("result", out JsonElement r) && r.ValueKind == JsonValueKind.Number)
                    {
                        if (r.GetUInt64() > 0)
                        {
                            Process.Note($"block header {r.GetUInt64()} after {(DateTime.UtcNow - start).TotalSeconds:0.0} s (D-044)");
                            return;
                        }
                        sawZero = true;
                    }
                    last = body;
                }
                catch (Exception e) when (e is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
                {
                    // The API may not answer yet during start-up; keep probing until the deadline.
                    last = e.GetType().Name + ": " + e.Message;
                }
                if (DateTime.UtcNow > deadline)
                {
                    // L18 (review R2 item 1): the infra label needs proof that the bot itself works. Only a bot whose
                    // API answered "height 0" is missing just the header; an API that never answered is a harness or
                    // bot failure (bad auth, lost port), not the testnet.
                    if (!sawZero)
                    {
                        throw new HarnessTimeoutException($"bot API never answered blockheight within {HarnessConfig.HeaderTimeout.TotalSeconds:0} s; not an infra failure. Last answer: {last}");
                    }
                    throw new HarnessInfraException($"{Markers.Tag(Markers.TestnetUnreachable, caseLabel)}: the bot API works but the bot has no block header after " +
                        $"{HarnessConfig.HeaderTimeout.TotalSeconds:0} s (seed={HarnessConfig.BotSeed}); infra failure, rerun the job. Last API answer: {last}");
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        /// <summary>GET http://localhost:api/method?k=v (Core GenericAPIServer.cs:139-190 reads the query string).</summary>
        public async Task<string> Api(string method, params (string Key, string Value)[] args)
        {
            string q = string.Join("&", args.Select(a => WebUtility.UrlEncode(a.Key) + "=" + WebUtility.UrlEncode(a.Value)));
            string url = $"http://localhost:{ApiPort}/{method}" + (q.Length > 0 ? "?" + q : "");
            HttpResponseMessage r = await http.GetAsync(url).ConfigureAwait(false);
            string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode || body.Contains("\"error\":{"))
            {
                throw new InvalidOperationException($"bot API {method} failed: {(int)r.StatusCode} {body}");
            }
            return body;
        }

        /// <summary>
        /// A fresh bot has no group and no channel. Chat on channel 0 is dropped (StreamProcessor.cs:379) and the
        /// price lookup reads the default group (:360-374). So: one default group at cost 0
        /// (APIServer.cs:369-396) and one default channel (:285-305; the first channel gets index 1,
        /// Core BotChannels.cs:227).
        /// </summary>
        public async Task SeedDefaultsAsync()
        {
            await Api("sb_newGroup", ("group", "members"), ("cost", "0"), ("admin", "0"), ("default", "1")).ConfigureAwait(false);
            await Api("sb_newChannel", ("channel", "general"), ("default", "1")).ConfigureAwait(false);
        }

        public string LogTail(int lines = 200)
        {
            var sb = new StringBuilder();
            foreach (string f in Directory.GetFiles(WorkDir, "ixian*.log"))
            {
                sb.AppendLine($"===== {Path.GetFileName(f)} (last {lines} lines) =====");
                try
                {
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    var all = sr.ReadToEnd().Split('\n');
                    foreach (string l in all.Skip(Math.Max(0, all.Length - lines)))
                    {
                        sb.AppendLine(l.TrimEnd('\r'));
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("(cannot read: " + e.Message + ")");
                }
            }
            return sb.ToString();
        }

        public void DisposeHttp() => http.Dispose();

        private static string StripAnsi(string s) => Regex.Replace(s, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");

        /// <summary>Two distinct free ports: both listeners are held until both ports are read (review R1 m6).</summary>
        private static (int, int) FreePortPair()
        {
            var a = new TcpListener(IPAddress.Loopback, 0);
            var b = new TcpListener(IPAddress.Loopback, 0);
            a.Start();
            b.Start();
            int pa = ((IPEndPoint)a.LocalEndpoint).Port;
            int pb = ((IPEndPoint)b.LocalEndpoint).Port;
            a.Stop();
            b.Stop();
            return (pa, pb);
        }

        private static async Task<bool> CanConnect(int port)
        {
            using var c = new TcpClient();
            try
            {
                await c.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Copies the build output without run state, so a bot that was once started from bin/ cannot leak an old
        /// log line, wallet, config or data into a test (review R1 m3).
        /// </summary>
        private static void CopyDir(string from, string to)
        {
            string[] skipDirs = { "Data", "activity" };
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(from, f);
                string top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                string name = Path.GetFileName(rel);
                if (skipDirs.Contains(top, StringComparer.OrdinalIgnoreCase)
                    || (rel == name && (name.StartsWith("ixian", StringComparison.OrdinalIgnoreCase)
                                        && (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)))))
                {
                    continue;
                }
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }
    }
}

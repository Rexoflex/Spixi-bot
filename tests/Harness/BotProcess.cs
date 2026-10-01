using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Harness
{
    /// <summary>
    /// The legacy bot (a5a3442 code, .NET 8, Core f6fb55b) as a child process (W11).
    /// Each instance gets a fresh copy of the bot output folder: the bot looks for its DLLs in the working
    /// folder and writes Data/, activity/ and its log there (Program.cs:33-41, Config.cs:95, Node.cs:235).
    /// Started with: -t (testnet), -n 127.0.0.1:1 (an unreachable seed: no DLT, F4), free -p/-a ports,
    /// -i 127.0.0.1, --disableWebStart, and --walletPassword (testnet only, Node.cs:120-126) so the bot makes
    /// its own wallet. The API login comes from ixian.cfg (addApiUser; no ':' in the password, Config.cs:207).
    /// </summary>
    internal sealed class BotProcess : IAsyncDisposable
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
            BotPort = FreePort();
            ApiPort = FreePort();
            File.WriteAllText(Path.Combine(workDir, "ixian.cfg"), $"addApiUser = {apiUser}:{apiPassword}\n");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiUser}:{apiPassword}")));
            Process = new ManagedProcess("bot", "dotnet", new[]
            {
                "SpixiBot.dll", "-t", "-n", "127.0.0.1:1", "-p", BotPort.ToString(), "-a", ApiPort.ToString(),
                "-i", "127.0.0.1", "--disableWebStart", "--walletPassword", "harness-bot-wallet-pw",
            }, workDir);
        }

        public static async Task<BotProcess> StartAsync(string runDir)
        {
            string work = Path.Combine(runDir, "bot");
            CopyDir(HarnessConfig.BotDir, work);
            var bot = new BotProcess(work);

            // The wallet address is printed on the line after "Your IXIAN addresses are:" (Node.cs:187-193),
            // with console colour codes around it.
            OutputLine header = await bot.Process.WaitFor(l => l.Text.Contains("Your IXIAN addresses are:"),
                HarnessConfig.StartTimeout, "wallet address header").ConfigureAwait(false);
            OutputLine addrLine = await bot.Process.WaitFor(l => l.At >= header.At && l.Stream == "stdout"
                    && Regex.IsMatch(StripAnsi(l.Text).Trim(), "^[1-9A-HJ-NP-Za-km-z]{40,}$"),
                HarnessConfig.StartTimeout, "wallet address").ConfigureAwait(false);
            bot.Address = StripAnsi(addrLine.Text).Trim();

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

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            await Process.DisposeAsync().ConfigureAwait(false);
        }

        private static string StripAnsi(string s) => Regex.Replace(s, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
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

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
            }
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
            }
        }
    }
}

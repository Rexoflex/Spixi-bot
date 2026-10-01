using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Storage;
using IXICore.Streaming;
using SimClient.Glue;

namespace SimClient
{
    /// <summary>
    /// SimClient — one simulated Spixi member (harness.md §2 option A, D-031).
    ///
    ///   SimClient --app store|redesign --data &lt;dir&gt; [--name &lt;nick&gt;] [--wallet-password &lt;pw&gt;]
    ///
    /// stdin  (one JSON object per line): {"cmd":"join","host":"127.0.0.1:port","address":"&lt;bot&gt;"}
    ///                                     {"cmd":"post","channel":1,"text":"hello"}
    ///                                     {"cmd":"quit"}
    /// stdout (one JSON object per line): ready, fatal, connected, hello_rejected, hello_attempts, join_sent,
    ///                                     accepted, info, channel, posted, ack, received, dropped, other, sent,
    ///                                     expired, stream_error, error, crashed, bye.
    /// Every member is a fresh testnet wallet in a fresh data folder (no wallet pool: session 3 decision).
    /// </summary>
    internal static class Program
    {
        private static readonly Dictionary<string, Friend> bots = new Dictionary<string, Friend>();
        private static readonly object botsLock = new object();
        private static Timer? repinTimer;
        private static string stage = "args";

        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // W12: a process death is an observable result (store app leaveConfirmed recursion).
                try { Events.Emit("crashed", new Dictionary<string, object?> { ["error"] = e.ExceptionObject?.ToString() }); } catch { }
            };

            string app = "redesign", data = "", name = "sim", walletPassword = "harness-wallet-pw";
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--app": app = args[i + 1]; break;
                    case "--data": data = args[i + 1]; break;
                    case "--name": name = args[i + 1]; break;
                    case "--wallet-password": walletPassword = args[i + 1]; break;
                    default: Console.Error.WriteLine("unknown option " + args[i]); return 64;
                }
            }
            if (data == "")
            {
                Console.Error.WriteLine("--data is required");
                return 64;
            }

            try
            {
                AppRules.Mode = AppRules.Parse(app);
                Start(data, name, walletPassword);
            }
            catch (Exception e)
            {
                // The stage names the start-up step that failed (CI run 36841419911: an exit before "ready").
                Events.Emit("fatal", new Dictionary<string, object?> { ["stage"] = stage, ["error"] = e.ToString() });
                Console.Error.WriteLine("SimClient fatal at stage " + stage + ": " + e);
                Environment.Exit(2);
                return 2;
            }

            CommandLoop();
            Events.Emit("bye");
            Logging.flush();
            // Core starts foreground threads (network queue, pending messages, local storage, client manager) and
            // has no single stop call for a client without the app's Node.stop(); returning from Main would leave
            // the process running (CI run 36844824431 hung here). Exit explicitly.
            Environment.Exit(0);
            return 0;
        }

        /// <summary>
        /// Startup = R Meta/Node.cs ctor (:109-164) + start() (:239-387), minus UI, push, mini-apps, TIV, block
        /// storage and the API server (no DLT, W10). Order kept where it matters: handler, wallet, client
        /// managers, stream processor, local storage, friend list, presence, queue.
        /// </summary>
        private static void Start(string data, string name, string walletPassword)
        {
            stage = "logging";
            Directory.CreateDirectory(data);
            Logging.setOptions(50, 10, false);                       // stdout is the event channel
            Logging.start(data);

            stage = "handler";
            var node = new SimNode();
            IxianHandler.init("simclient-0.1", node, NetworkType.test, false);   // R :118 (testnet here)

            stage = "wallet";
            Stopwatch sw = Stopwatch.StartNew();
            WalletStorage ws = new WalletStorage(Path.Combine(data, "wallet.ixi"));
            if (!ws.generateWallet(walletPassword))
            {
                throw new Exception("wallet generation failed");
            }
            IxianHandler.addWallet(ws);
            long keygenMs = sw.ElapsedMilliseconds;

            stage = "client managers";
            PeerStorage.init(data);                                                   // R :125
            NetworkClientManager.init(new NetworkClientManagerStatic(3));             // R :128-129, never started: no seeds; Core requires >= 3 (NetworkClientManagerBase.cs:53-56, CI run 36844069755)
            StreamClientManager.init(4, false);                                       // R :130 (no random S2 nodes here)
            stage = "stream processor";
            SimNode.streamProcessor = new SimStreamProcessor(new SimPendingMessageProcessor(data),
                StreamCapabilities.Incoming | StreamCapabilities.Outgoing);           // R :133-134 (IPN/Apps dropped: no push, no apps)
            stage = "local storage";
            IxianHandler.localStorage = new LocalStorage(data, new SimLocalStorageCallbacks()); // R :142
            FriendList.init(data, true);                                              // R :147
            IxianHandler.localStorage.start();                                        // R preStart :201
            FriendList.loadContacts();                                                // R preStart :229

            stage = "presence";
            PresenceList.init(IxianHandler.publicIP, 0, 'C', CoreConfig.clientKeepAliveInterval); // R :349
            stage = "start threads";
            NetworkQueue.start();                                                     // R :352
            SimNode.streamProcessor.start();                                          // R :354
            StreamClientManager.start();

            SimStreamProcessor.StreamErrorReceived += addr => Repin();
            repinTimer = new Timer(_ => Repin(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            stage = "ready";
            Events.Emit("ready", new Dictionary<string, object?>
            {
                ["address"] = ws.getPrimaryAddress().ToString(),
                ["app"] = AppRules.Mode.ToString().ToLowerInvariant(),
                ["keygenMs"] = keygenMs,
                ["name"] = name,
            });
        }

        private static void CommandLoop()
        {
            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    JsonElement root = doc.RootElement;
                    string cmd = root.GetProperty("cmd").GetString() ?? "";
                    switch (cmd)
                    {
                        case "join":
                            Join(root.GetProperty("host").GetString()!, root.GetProperty("address").GetString()!).GetAwaiter().GetResult();
                            break;
                        case "post":
                            Post(root.GetProperty("channel").GetInt32(), root.GetProperty("text").GetString() ?? "");
                            break;
                        case "quit":
                            return;
                        default:
                            Events.Emit("error", new Dictionary<string, object?> { ["where"] = "command", ["error"] = "unknown cmd " + cmd });
                            break;
                    }
                }
                catch (Exception e)
                {
                    Events.Error("command", e);
                }
            }
        }

        /// <summary>
        /// Join with the direct-join divergence W9 (harness.md §4). The app adds the bot and sends requestAdd2
        /// (AppRules.AddBotContact); it only connects after a presence update fills relayNode (R Meta/Node.cs
        /// :465-484 connectToBotNodes). The harness has no presence network, so it:
        ///   1. calls setBotMode() before the request (the app does this only on acceptAddBot, C :2089), so the
        ///      first requestAdd2 is not sent to the push server (C PendingMessageProcessor.cs:486-488);
        ///   2. pins online/updatedStreamingNodes/relayNode=null so PendingMessageProcessor sends directly to the
        ///      connected bot (C PendingMessageProcessor.cs:437-460);
        ///   3. connects by host and bot address (C StreamClientManager.connectTo) and waits for the bot's hello;
        ///   4. re-pins every 30 s and after a stream error (C CoreStreamProcessor.cs:719-724 resets the pins).
        /// </summary>
        private static async Task Join(string host, string address)
        {
            Address bot = new Address(address);
            Friend? friend = AppRules.AddBotContact(bot, "bot");
            if (friend == null)
            {
                Events.Emit("error", new Dictionary<string, object?> { ["where"] = "join", ["error"] = "already a contact" });
                return;
            }
            friend.setBotMode();
            lock (botsLock)
            {
                bots[address] = friend;
            }
            Repin();

            var hello = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<Address> onHello = a => { if (a.SequenceEqual(bot)) hello.TrySetResult(true); };
            SimNode.BotHelloCompleted += onHello;
            try
            {
                // The app retries the bot connection every 2.5 s (connectToBotNodes, R Meta/Node.cs:465-484 from the
                // loop at :614; U Meta/Node.cs:293-311). The bot answers "bye: not ready" until its TIV has a block
                // header (Core f6fb55b CoreNetworkProtocol.cs:509-514); CI run 36846606694 connected once, 1.6 s
                // after bot start, got that bye and never retried. So: retry like the app until hello or 30 s.
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                int attempts = 0;
                while (!hello.Task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    attempts++;
                    await StreamClientManager.connectTo(host, bot).ConfigureAwait(false);
                    await Task.WhenAny(hello.Task, Task.Delay(TimeSpan.FromMilliseconds(2500))).ConfigureAwait(false);
                }
                if (!hello.Task.IsCompleted)
                {
                    Events.Emit("error", new Dictionary<string, object?> { ["where"] = "join", ["error"] = "no hello from the bot within 30 s", ["attempts"] = attempts });
                    return;
                }
                Events.Emit("hello_attempts", new Dictionary<string, object?> { ["attempts"] = attempts });
            }
            finally
            {
                SimNode.BotHelloCompleted -= onHello;
            }
            Repin();
            CoreStreamProcessor.sendContactRequest(friend);
            Events.Emit("join_sent", new Dictionary<string, object?> { ["bot"] = address });
        }

        private static void Post(int channel, string text)
        {
            Friend bot;
            lock (botsLock)
            {
                if (bots.Count != 1)
                {
                    throw new InvalidOperationException("post needs exactly one joined bot, have " + bots.Count);
                }
                bot = new List<Friend>(bots.Values)[0];
            }
            FriendMessage? fm = AppRules.Post(bot, channel, text);
            Events.Emit("posted", new Dictionary<string, object?>
            {
                ["id"] = fm?.id == null ? null : Crypto.hashToString(fm.id),
                ["channel"] = channel,
                ["text"] = text,
            });
        }

        private static void Repin()
        {
            List<Friend> list;
            lock (botsLock)
            {
                list = new List<Friend>(bots.Values);
            }
            foreach (Friend f in list)
            {
                f.online = true;
                f.updatedStreamingNodes = Clock.getNetworkTimestamp();
                f.relayNode = null;
            }
        }
    }
}

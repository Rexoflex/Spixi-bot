using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.SpixiBot;
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
    ///                                     {"cmd":"refresh"}
    ///                                     {"cmd":"set-cursor","channel":1,"id":"&lt;hex&gt;"}
    ///                                     {"cmd":"react","channel":1,"id":"&lt;hex&gt;","reaction":"like:"}
    ///                                     {"cmd":"delete","channel":1,"id":"&lt;hex&gt;"}
    ///                                     {"cmd":"leave"}
    ///                                     {"cmd":"inject","kind":"chat|unknown-code|unknown-action|info-k|info-legacy",...}
    ///                                     {"cmd":"quit"}
    /// stdout (one JSON object per line): ready, fatal, connected, hello_rejected, hello_attempts, join_sent,
    ///                                     accepted, info, channel, user, bot_action, posted, ack, received,
    ///                                     reaction, deleted, rejected, reacted, delete_sent, leave_sent, injected,
    ///                                     refresh_sent, cursor_set, dropped, other, sent, expired, stream_error,
    ///                                     error, crashed, bye.
    /// Every member is a fresh testnet wallet in a fresh data folder (no wallet pool: session 3 decision).
    /// </summary>
    internal static class Program
    {
        private static readonly Dictionary<string, Friend> bots = new Dictionary<string, Friend>();
        private static readonly object botsLock = new object();
        private static Timer? repinTimer;
        private static SimNode? simNode;
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
            simNode = node;
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
                        case "refresh":
                            Refresh();
                            break;
                        case "set-cursor":
                            SetCursor(root.GetProperty("channel").GetInt32(), root.GetProperty("id").GetString() ?? "");
                            break;
                        case "react":
                            React(root.GetProperty("channel").GetInt32(), root.GetProperty("id").GetString() ?? "", root.GetProperty("reaction").GetString() ?? "");
                            break;
                        case "delete":
                            Delete(root.GetProperty("channel").GetInt32(), root.GetProperty("id").GetString() ?? "");
                            break;
                        case "leave":
                            Leave();
                            break;
                        case "inject":
                            Inject(root);
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
            Friend bot = SingleBot();
            FriendMessage? fm = AppRules.Post(bot, channel, text);
            Events.Emit("posted", new Dictionary<string, object?>
            {
                ["id"] = fm?.id == null ? null : Crypto.hashToString(fm.id),
                ["channel"] = channel,
                ["text"] = text,
            });
        }

        private static Friend SingleBot()
        {
            lock (botsLock)
            {
                if (bots.Count != 1)
                {
                    throw new InvalidOperationException("this command needs exactly one joined bot, have " + bots.Count);
                }
                return new List<Friend>(bots.Values)[0];
            }
        }

        /// <summary>
        /// B1a history scenario: replay the app's new-connection cascade on the open connection. Both apps send
        /// getInfo on every new connection to a bot (U/R NetworkProtocol.cs:109-112, SimNode.cs copies it); the
        /// bot answers info, Core asks getChannels, and every `channel` makes Core send botGetMessages with the
        /// stored cursor (C CoreStreamProcessor.cs:2660-2673). Divergence (marked in the README): no TCP
        /// reconnect, so the bot's per-connection state is not reset; the bot keeps none for botGetMessages
        /// (Messages.cs:196-220 reads only the cursor).
        /// </summary>
        private static void Refresh()
        {
            Friend bot = SingleBot();
            // Report the stored cursors at refresh time (Core reads the same dictionary when the `channel` arrives,
            // C :2664-2671), so a test can prove which cursor the bot received (review R2 item 4).
            var cursors = new Dictionary<string, object?>();
            lock (bot.metaData.lastReceivedMessageIds)
            {
                foreach (var kv in bot.metaData.lastReceivedMessageIds)
                {
                    cursors[kv.Key.ToString()] = kv.Value == null ? null : Crypto.hashToString(kv.Value);
                }
            }
            CoreStreamProcessor.sendGetBotInfo(bot);
            Events.Emit("refresh_sent", new Dictionary<string, object?> { ["cursors"] = cursors });
        }

        /// <summary>
        /// B1a history scenario: overwrite the stored cursor for a channel (Core FriendMetaData
        /// setLastReceivedMessageIds, Friend.cs:132-142). An id the bot does not know models the cases in
        /// research D §4 (own unrelayed message, pruned beyond 10 000, bot DB reset).
        /// </summary>
        private static void SetCursor(int channel, string hexId)
        {
            Friend bot = SingleBot();
            byte[] id = Convert.FromHexString(hexId);
            if (!bot.metaData.setLastReceivedMessageIds(id, channel))
            {
                throw new InvalidOperationException("cursor not set: the bot friend has no botInfo yet");
            }
            Events.Emit("cursor_set", new Dictionary<string, object?> { ["channel"] = channel, ["id"] = Crypto.hashToString(id) });
        }

        /// <summary>B1a react/delete scenario: react to a stored message (AppRules.React, U/R "like" handler).</summary>
        private static void React(int channel, string hexId, string reaction)
        {
            Friend bot = SingleBot();
            byte[] id = Convert.FromHexString(hexId);
            bool added = AppRules.React(bot, id, reaction, channel);
            Events.Emit("reacted", new Dictionary<string, object?> { ["id"] = Crypto.hashToString(id), ["channel"] = channel, ["localAdded"] = added });
        }

        /// <summary>B1a react/delete scenario: ask the bot to delete a message (AppRules.Delete, U/R "deleteMessage").</summary>
        private static void Delete(int channel, string hexId)
        {
            Friend bot = SingleBot();
            byte[] id = Convert.FromHexString(hexId);
            AppRules.Delete(bot, id, channel);
            Events.Emit("delete_sent", new Dictionary<string, object?> { ["id"] = Crypto.hashToString(id), ["channel"] = channel });
        }

        /// <summary>
        /// B1a leave scenario (AppRules.Leave). Store: the friend stays with pendingDeletion, so it stays pinned.
        /// Redesign: the friend is removed at once, so SimClient also stops re-pinning it (it is gone from `bots`).
        /// </summary>
        private static void Leave()
        {
            Friend bot = SingleBot();
            bool removed = AppRules.Leave(bot, out bool sent);
            if (AppRules.Mode == AppMode.Redesign)
            {
                lock (botsLock)
                {
                    foreach (string key in new List<string>(bots.Keys))
                    {
                        if (bots[key] == bot)
                        {
                            bots.Remove(key);
                        }
                    }
                }
            }
            Events.Emit("leave_sent", new Dictionary<string, object?>
            {
                ["app"] = AppRules.Mode.ToString().ToLowerInvariant(),
                ["removed"] = removed,
                ["sent"] = sent,
            });
        }

        /// <summary>
        /// B1a info/unknown scenarios: deliver a message "from the bot" that the bot under test cannot be made to
        /// send. The StreamMessage is built like the bot's sendBotAction (bot Network/StreamProcessor.cs:498-512:
        /// type info, sender = bot, recipient = member, encryption none, unsigned, data = SpixiMessage bytes) and
        /// enters through SimNode.parseProtocolMessage(s2data, bytes, endpoint) with the bot's real connection,
        /// the same entry a wire message uses. Divergence (README): no TCP crossing. Runs synchronously on the
        /// command thread, so every event Core and SimStreamProcessor emit synchronously inside receiveData is
        /// printed before `injected`. The bot's answers to requests Core sends during that call (getGroups,
        /// getUsers, getChannels after an accepted info, C :2676-2707) arrive later on the network thread and may
        /// be printed after `injected`.
        /// </summary>
        private static void Inject(JsonElement root)
        {
            string kind = root.GetProperty("kind").GetString() ?? "";
            Friend? bot = null;
            lock (botsLock)
            {
                if (bots.Count == 1)
                {
                    bot = new List<Friend>(bots.Values)[0];
                }
            }
            if (bot == null)
            {
                InjectError("inject needs exactly one joined bot");
                return;
            }
            BotInfo? stored = bot.metaData.botInfo;
            if (stored == null)
            {
                InjectError("the bot friend has no botInfo yet");
                return;
            }
            RemoteEndpoint? endpoint = SimNode.GetBotEndpoint(bot.walletAddress);
            if (endpoint == null || simNode == null)
            {
                InjectError("no connection to the bot (no helloData seen)");
                return;
            }

            SpixiMessage sm;
            switch (kind)
            {
                case "chat":
                    sm = new SpixiMessage(SpixiMessageCode.chat, Encoding.UTF8.GetBytes(root.GetProperty("text").GetString() ?? ""), root.GetProperty("channel").GetInt32());
                    break;

                case "unknown-code":
                    sm = new SpixiMessage((SpixiMessageCode)root.GetProperty("code").GetInt32(), new byte[] { 1, 2, 3 }, root.GetProperty("channel").GetInt32());
                    break;

                case "unknown-action":
                    sm = new SpixiMessage(SpixiMessageCode.botAction,
                        new SpixiBotAction((SpixiBotActionCode)root.GetProperty("action").GetInt32(), new byte[] { 1 }).getBytes(), 0);
                    break;

                case "info-k":
                    {
                        // Core k layout (BotInfo.cs:33 ctor, :79-100 getBytes): adds randomId + hideParticipantAddresses.
                        byte[]? randomId = null;
                        if (root.TryGetProperty("randomId", out JsonElement rid) && rid.ValueKind == JsonValueKind.String
                            && !string.IsNullOrEmpty(rid.GetString()))
                        {
                            randomId = Convert.FromHexString(rid.GetString()!);
                        }
                        BotInfo bi = new BotInfo(stored.version, randomId, root.GetProperty("hide").GetBoolean(),
                            stored.serverDescription, stored.cost, stored.settingsGeneratedTime + 1, stored.admin,
                            stored.defaultGroup, stored.defaultChannel, stored.sendNotification, stored.userCount);
                        bi.serverName = root.GetProperty("serverName").GetString() ?? "";
                        byte[] info = WithTrailing(bi.getBytes(), root.GetProperty("trailing").GetInt32());
                        sm = new SpixiMessage(SpixiMessageCode.botAction, new SpixiBotAction(SpixiBotActionCode.info, info).getBytes(), 0);
                    }
                    break;

                case "info-legacy":
                    {
                        // The bot's Core f6fb55b layout (Streaming/Bot/BotInfo.cs:72-91, writes :78-87): no randomId.
                        byte[] info;
                        using (MemoryStream m = new MemoryStream())
                        {
                            using (BinaryWriter writer = new BinaryWriter(m))
                            {
                                writer.Write(stored.version);
                                writer.Write(root.GetProperty("serverName").GetString() ?? "");
                                writer.Write(stored.serverDescription ?? "");
                                writer.Write(stored.cost.ToString());
                                writer.Write(stored.settingsGeneratedTime + 1);
                                writer.Write(stored.admin);
                                writer.Write(stored.defaultGroup);
                                writer.Write(stored.defaultChannel);
                                writer.Write(stored.sendNotification);
                                writer.Write(stored.userCount);
                            }
                            info = m.ToArray();
                        }
                        info = WithTrailing(info, root.GetProperty("trailing").GetInt32());
                        sm = new SpixiMessage(SpixiMessageCode.botAction, new SpixiBotAction(SpixiBotActionCode.info, info).getBytes(), 0);
                    }
                    break;

                default:
                    InjectError("unknown inject kind " + kind);
                    return;
            }

            StreamMessage msg = new StreamMessage();
            msg.type = StreamMessageCode.info;
            msg.sender = bot.walletAddress;
            msg.recipient = IxianHandler.getWalletStorage().getPrimaryAddress();
            msg.data = sm.getBytes();
            msg.encryptionType = StreamMessageEncryptionCode.none;

            simNode.parseProtocolMessage(ProtocolMessageCode.s2data, msg.getBytes(), endpoint);
            Events.Emit("injected", new Dictionary<string, object?> { ["kind"] = kind, ["id"] = Crypto.hashToString(msg.id) });
        }

        private static void InjectError(string error)
        {
            Events.Emit("error", new Dictionary<string, object?> { ["where"] = "inject", ["error"] = error });
        }

        /// <summary>Appends `trailing` bytes of 0xAB (as a varint length, 0xAB makes ReadIxiBytes read past the end).</summary>
        private static byte[] WithTrailing(byte[] bytes, int trailing)
        {
            if (trailing < 0)
            {
                throw new ArgumentException("trailing must be >= 0");
            }
            byte[] result = new byte[bytes.Length + trailing];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            for (int i = bytes.Length; i < result.Length; i++)
            {
                result[i] = 0xAB;
            }
            return result;
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

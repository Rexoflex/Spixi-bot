using System;
using System.Collections.Generic;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.SpixiBot;
using IXICore.Streaming;

namespace SimClient.Glue
{
    /// <summary>
    /// The app's stream processor reduced to what the bot path needs (harness.md W10). The apps' own class
    /// (U/R Network/StreamProcessor.cs, ~800 lines, MAUI-bound) calls base.receiveData and then switches on the
    /// type (R :277-300, U :198-215). Here every handled type also becomes an event on stdout.
    /// Not copied (NO-OP here): files, VoIP, mini-apps, funds, typing, avatars, nick UI, local notifications,
    /// and the non-bot contact flows (requestAdd/acceptAdd/keys2).
    /// </summary>
    internal class SimStreamProcessor : CoreStreamProcessor
    {
        public SimStreamProcessor(PendingMessageProcessor pmp, StreamCapabilities caps) : base(pmp, caps)
        {
        }

        /// <summary>A stream error resets the friend's pins (Core :719-724); the join loop re-pins (W9).</summary>
        public static event Action<Address>? StreamErrorReceived;

        public override ReceiveDataResponse? receiveData(byte[] bytes, RemoteEndpoint endpoint, bool fireLocalNotification = true, bool alert = true)
        {
            StreamMessage? peek = null;
            try { peek = new StreamMessage(bytes); } catch (Exception) { }

            ReceiveDataResponse? rdr = base.receiveData(bytes, endpoint, fireLocalNotification, alert);

            if (peek != null && peek.type == StreamMessageCode.error)
            {
                Events.Emit("stream_error", new Dictionary<string, object?> { ["from"] = peek.sender?.ToString() });
                if (peek.sender != null)
                {
                    StreamErrorReceived?.Invoke(peek.sender);
                }
            }
            if (rdr == null)
            {
                return null;
            }

            try
            {
                SpixiMessage sm = rdr.spixiMessage;
                StreamMessage msg = rdr.streamMessage;
                Friend? friend = rdr.friend;
                switch (sm.type)
                {
                    case SpixiMessageCode.acceptAddBot:
                        // U :366-378, R :567-579: a local "accepted" row + convertToBot (UI only).
                        Events.Emit("accepted", new Dictionary<string, object?> { ["bot"] = rdr.senderAddress.ToString(), ["isBot"] = friend?.bot });
                        break;

                    case SpixiMessageCode.botAction:
                        // Core onBotAction already handled it (C CoreStreamProcessor.cs:2645-2736); U :380, R :581 only
                        // add kick/ban rows. The harness needs the channel list and the info.
                        SpixiBotAction sba = new SpixiBotAction(sm.data);
                        if (sba.action == SpixiBotActionCode.channel)
                        {
                            BotChannel ch = new BotChannel(sba.data);
                            Events.Emit("channel", new Dictionary<string, object?> { ["index"] = ch.index, ["name"] = ch.channelName });
                        }
                        else if (sba.action == SpixiBotActionCode.info)
                        {
                            Events.Emit("info", new Dictionary<string, object?> { ["defaultChannel"] = friend?.metaData?.botInfo?.defaultChannel });
                        }
                        else if (sba.action == SpixiBotActionCode.user)
                        {
                            // Core onBotAction stored it in the roster (C :2710-2719). B1a join handshake: the bot
                            // answers getUsers with one `user` per normal member (bot StreamProcessor.cs sendUsers).
                            BotContact bc = new BotContact(sba.data, false);
                            Events.Emit("user", new Dictionary<string, object?> { ["address"] = bc.publicKey == null ? null : new Address(bc.publicKey).ToString() });
                        }
                        else
                        {
                            Events.Emit("bot_action", new Dictionary<string, object?> { ["action"] = sba.action.ToString() });
                        }
                        break;

                    case SpixiMessageCode.chat:
                        {
                            string? text = AppRules.DecodeChatText(sm.data);
                            if (text == null)
                            {
                                Events.Emit("dropped", new Dictionary<string, object?> { ["reason"] = "store app: null chat data throws" });
                                break;
                            }
                            // U :410-411, R :613-616: store the row (the app's Node.addMessageWithType → Core FriendList).
                            // Returns null when the id is already stored (own echo, history replay of a known message):
                            // Core FriendList.cs:258-266 dedups by id and sequence. `stored` reports it (research D §8 item 3).
                            FriendMessage? stored = FriendList.addMessageWithType(msg.id, FriendMessageType.standard, rdr.senderAddress, sm.channel, text, false, rdr.groupSenderAddress, msg.timestamp, fireLocalNotification, 0);
                            Address? author = rdr.groupSenderAddress ?? rdr.senderAddress;
                            Events.Emit("received", new Dictionary<string, object?>
                            {
                                ["id"] = msg.id == null ? null : Crypto.hashToString(msg.id),
                                ["channel"] = sm.channel,
                                ["from"] = author?.ToString(),
                                ["self"] = author != null && IxianHandler.getWalletStorage().isMyAddress(author),
                                ["text"] = text,
                                ["stored"] = stored != null,
                            });
                        }
                        break;

                    case SpixiMessageCode.msgReceived:
                        // U :447-457, R :659-697: UI tick only. Core removed the pending message (C :486-608).
                        Events.Emit("ack", new Dictionary<string, object?>
                        {
                            ["id"] = sm.data == null ? null : Crypto.hashToString(sm.data),
                            ["channel"] = sm.channel,
                        });
                        break;

                    default:
                        Events.Emit("other", new Dictionary<string, object?> { ["type"] = sm.type.ToString() });
                        break;
                }
            }
            catch (Exception e)
            {
                // The apps catch in receiveData too (U/R outer catch); the harness sees it as an event.
                Logging.error("SimStreamProcessor: " + e);
                Events.Error("receiveData", e);
            }
            return rdr;
        }
    }
}

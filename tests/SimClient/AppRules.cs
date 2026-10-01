using System;
using System.Text;
using IXICore;
using IXICore.Meta;
using IXICore.Streaming;

namespace SimClient
{
    internal enum AppMode { Store, Redesign }

    /// <summary>
    /// App-layer rules the bot depends on, copied from the two apps (harness.md W4). Every rule cites the app
    /// line it copies: U = store app Spixi @ 0e85a4b (spixi-0.9.22), R = redesign @ 5d48669. A rule without a
    /// citation fails review. Both modes compile against Core k (W3, hypothesis until BE-04).
    ///
    /// Rules that are the same in both apps are written once and cite both. Rules that differ switch on Mode.
    /// </summary>
    internal static class AppRules
    {
        public static AppMode Mode = AppMode.Redesign;

        /// <summary>
        /// Join = add the bot as a contact and send a contact request.
        /// U Pages/Home/HomePage.xaml.cs:600-607 and R :2284-2292 are identical:
        /// addFriend(Normal, RequestSent, bot, null, name, null, null, 0) → save() → sendContactRequest(friend).
        /// (R also removes the address from its ignore list first, :2291; no effect on the wire.)
        /// </summary>
        public static Friend? AddBotContact(Address bot, string name)
        {
            Friend? friend = FriendList.addFriend(FriendType.Normal, FriendState.RequestSent, bot, null, name, null, null, 0);
            friend?.save();
            return friend;
        }

        /// <summary>
        /// Post = store the message locally, then send it.
        /// U Pages/Chat/SingleChatPage.xaml.cs:637-646 and R :1643-1661: SpixiMessage(chat, UTF-8 text, channel)
        /// sizes the payable length; Node.addMessageWithType(null, standard, bot, channel, text, true, null, 0,
        /// true, true, len) creates the FriendMessage (its id becomes the StreamMessage id); then
        /// CoreStreamProcessor.sendChatMessage(friend, friend_message, channel).
        /// Difference: R refuses to send when the local store returns null (R :1645-1649); U passes the null on,
        /// and sendChatMessage then throws (Core CoreStreamProcessor.cs:2203). Bot paid-room checks (U :618-635,
        /// R :1586-1600) are skipped: the harness bot runs with cost 0.
        /// The app's Node.addMessageWithType wrapper adds UI work only; the store call is
        /// FriendList.addMessageWithType (Core FriendList.cs:162).
        /// </summary>
        public static FriendMessage? Post(Friend bot, int channel, string text)
        {
            text = text.Trim(new char[] { ' ', '\t', '\r', '\n' });   // U SingleChatPage.xaml.cs:612-616, R :1580-1584 (empty → no send)
            if (text.Length < 1)
            {
                return null;
            }
            SpixiMessage spixi_message = new SpixiMessage(SpixiMessageCode.chat, Encoding.UTF8.GetBytes(text), channel);
            int payable_len = spixi_message.getBytes().Length;
            FriendMessage? fm = FriendList.addMessageWithType(null, FriendMessageType.standard, bot.walletAddress, channel, text, true, null, 0, true, payable_len);
            if (fm == null && Mode == AppMode.Redesign)
            {
                throw new InvalidOperationException("R: local store returned null; the app does not send (R SingleChatPage.xaml.cs:1645-1649).");
            }
            CoreStreamProcessor.sendChatMessage(bot, fm!, channel);
            return fm;
        }

        /// <summary>
        /// React = store the reaction locally, and send it only if Core stored it.
        /// U Pages/Chat/SingleChatPage.xaml.cs:1045-1058 and R :2521-2541 are the same on the wire:
        /// address = own primary address; friend.addReaction(address, new ReactionMessage(msg_id, "like:"), channel);
        /// only when that returns true → updateReactions (UI) and StreamProcessor.sendReaction(friend, msg_id,
        /// "like:", channel). The apps only send "like:"; the harness passes the reaction text through (Core k
        /// Friend.cs:983-1044 accepts tip:/like:/received:/seen:/fileReceived:, max 32 chars, target stored).
        /// The blind-group branch (U :1047-1053, R :2528-2535) applies only to FriendType.Group; a bot friend is
        /// FriendType.Normal (AddBotContact), so the address stays the primary address in both apps.
        /// Returns whether Core stored the reaction locally (and so whether it was sent).
        /// </summary>
        public static bool React(Friend friend, byte[] msgId, string reaction, int channel)
        {
            Address address = IxianHandler.getWalletStorage().getPrimaryAddress();
            if (!friend.addReaction(address, new ReactionMessage(msgId, reaction), channel))
            {
                return false;
            }
            CoreStreamProcessor.sendReaction(friend, msgId, reaction, channel);   // Core k CoreStreamProcessor.cs:2882-2887
            return true;
        }

        /// <summary>
        /// Delete = send msgDelete; delete locally only for a non-bot friend.
        /// U Pages/Chat/SingleChatPage.xaml.cs:1034-1043 and R :2494-2519: StreamProcessor.sendMsgDelete(friend,
        /// msg_id, channel); then `if (!friend.bot) friend.deleteMessage(...)`. For a bot the local row goes only
        /// when the bot relays the delete back (Core k CoreStreamProcessor.cs:1349-1366 → handleMsgDelete :1665).
        /// R's own-file-offer cancel (R :2501-2507) is a file-transfer rule: NO-OP here (no files).
        /// </summary>
        public static void Delete(Friend friend, byte[] msgId, int channel)
        {
            CoreStreamProcessor.sendMsgDelete(friend, msgId, channel);           // Core k CoreStreamProcessor.cs:2852-2857
            if (!friend.bot)
            {
                friend.deleteMessage(msgId, channel);                            // Core k Friend.cs:949
            }
        }

        /// <summary>
        /// Leave the bot. The apps differ:
        /// Store: U Pages/Contacts/ContactDetails.xaml.cs onRemove :122-146. A bot with botInfo is NOT removed:
        ///   pendingDeletion = true; save(); CoreStreamProcessor.sendLeave(friend, null). The friend stays until the
        ///   bot's leaveConfirmed arrives. A bot without botInfo goes through FriendList.removeFriend (no leave sent).
        /// Redesign: R Utils/SContacts.cs leaveGroup :70-119. sendLeave(group, null) inside try/catch (R :97-105; a
        ///   throw is logged by type only), then FriendList.removeFriend(group) at once (R :108; #567: no
        ///   pendingDeletion wait).
        /// `sent` reports whether sendLeave was called without a throw; the return value is whether the friend
        /// was removed from the friend list.
        /// </summary>
        public static bool Leave(Friend friend, out bool sent)
        {
            sent = false;
            if (Mode == AppMode.Store)
            {
                if (friend.bot && friend.metaData.botInfo != null)
                {
                    friend.pendingDeletion = true;                               // U :126
                    friend.save();                                               // U :127
                    CoreStreamProcessor.sendLeave(friend, null);                 // U :129, Core k CoreStreamProcessor.cs:2916-2922
                    sent = true;
                    return false;
                }
                return FriendList.removeFriend(friend);                          // U :135
            }

            if (!friend.bot && friend.type != FriendType.Group)                 // R :72-75
            {
                return false;
            }
            try
            {
                CoreStreamProcessor.sendLeave(friend, null);                     // R :97-105 (try/catch)
                sent = true;
            }
            catch (Exception ex)
            {
                Logging.warn("leaveGroup: the leave notice could not be sent (" + ex.GetType().Name + ")");   // R :101-105
            }
            return FriendList.removeFriend(friend);                              // R :108, Core k FriendList.cs:429-460
        }

        /// <summary>
        /// Received chat text. U Network/StreamProcessor.cs:410-411 decodes with Encoding.UTF8.GetString(data),
        /// which throws on null data (the app's outer catch drops the message). R :613-616 uses safeString
        /// (R :1310-1320): null or empty → "" and the row is still stored.
        /// Returns null when the app would drop the message.
        /// </summary>
        public static string? DecodeChatText(byte[]? data)
        {
            if (Mode == AppMode.Store)
            {
                return data == null ? null : Encoding.UTF8.GetString(data);
            }
            if (data == null || data.Length == 0)
            {
                return "";
            }
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static AppMode Parse(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "store": return AppMode.Store;
                case "redesign": return AppMode.Redesign;
                default: throw new ArgumentException("--app must be store or redesign, not '" + s + "'");
            }
        }
    }
}

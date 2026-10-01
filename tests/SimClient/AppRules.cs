using System;
using System.Text;
using IXICore;
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

using System.Collections.Generic;
using IXICore;
using IXICore.Storage;
using IXICore.Streaming;

namespace SimClient.Glue
{
    /// <summary>
    /// Pending-message hooks (Core k Streaming/PendingMessageProcessor.cs:647-648). The apps use them for UI
    /// ticks and push bookkeeping (R SpixiPendingMessageProcessor). Here they only become events.
    /// Push to the S2 push server is OFF (enable_push_notification_server = false); bots never use it anyway
    /// (Core PendingMessageProcessor.cs:486-488).
    /// </summary>
    internal class SimPendingMessageProcessor : PendingMessageProcessor
    {
        public SimPendingMessageProcessor(string root_storage_path) : base(root_storage_path, false)
        {
        }

        protected override void onMessageSent(Friend friend, int channel, StreamMessage msg)
        {
            Events.Emit("sent", new Dictionary<string, object?> { ["id"] = msg.id == null ? null : Crypto.hashToString(msg.id), ["channel"] = channel });
        }

        protected override void onMessageExpired(Friend friend, int channel, StreamMessage msg)
        {
            Events.Emit("expired", new Dictionary<string, object?> { ["id"] = msg.id == null ? null : Crypto.hashToString(msg.id), ["channel"] = channel });
        }
    }

    /// <summary>LocalStorage callback (Core k Streaming/Storage/LocalStorage.cs:24-27). NO-OP: the apps render UI here.</summary>
    internal class SimLocalStorageCallbacks : LocalStorageCallbacks
    {
        public void processMessage(Friend friend, int channel, FriendMessage friendMessage)
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using IXICore;
using IXICore.Meta;
using IXICore.Network;
using IXICore.RegNames;
using IXICore.Streaming;
using IXICore.Utils;

namespace SimClient.Glue
{
    /// <summary>
    /// Minimum IxianNode for a client with no DLT (harness.md W10, F5). The 12 abstract members of Core k
    /// Meta/IxianNode.cs:59-73. Everything block-, transaction- and name-related is a NO-OP: the bot spike
    /// never pays and never verifies blocks (cost 0, D-041 operator rule; paid rooms are broken on both apps,
    /// research D §5).
    /// </summary>
    internal class SimNode : IxianNode
    {
        public static SimStreamProcessor? streamProcessor;

        /// <summary>Set when a hello from a bot friend completes; the join waits on it.</summary>
        public static event Action<Address>? BotHelloCompleted;

        public override Block? getBlockHeader(ulong blockNum) => null;              // NO-OP: no DLT
        public override byte[]? getBlockHash(ulong blockNum) => null;              // NO-OP: no DLT
        public override Block? getLastBlock() => null;                             // NO-OP: no DLT
        public override ulong getLastBlockHeight() => 0;                           // NO-OP: no DLT
        public override int getLastBlockVersion() => 0;                            // NO-OP: no DLT (hello carries it)
        public override bool addIncomingTransaction(Transaction tx) => false;      // NO-OP: no payments
        public override bool addTransaction(Transaction tx, List<Address> relayNodeAddresses, List<ExtendedAddress>? extendedAddresses, byte[]? requestId, bool force_broadcast) => false; // NO-OP
        public override bool isAcceptingConnections() => false;                    // clients do not listen (R Node uses no NetworkServer)
        public override void shutdown() { }                                         // NO-OP: the process exits on 'quit' or stdin EOF
        public override IxiNumber getMinSignerPowDifficulty(ulong blockNum, int curBlockVersion, long curBlockTimestamp) => new IxiNumber(0); // NO-OP
        public override RegisteredNameRecord getRegName(byte[] name, bool useAbsoluteId) => null!;                                          // NO-OP

        /// <summary>
        /// Copy of the protocol codes the bot path needs, from R Network/NetworkProtocol.cs (identical in U):
        /// hello :26-37, helloData :41-123 (only the 'C'-bot branch :105-112 matters here; the 'R'/'M'/'H'
        /// branches serve DLT and S2 nodes, which the harness does not run), s2data :125-128.
        /// Every other code (presence, balance, keepalive, transactions, blocks) is a NO-OP here.
        /// </summary>
        public override void parseProtocolMessage(ProtocolMessageCode code, byte[] data, RemoteEndpoint endpoint)
        {
            try
            {
                switch (code)
                {
                    case ProtocolMessageCode.hello:
                        using (MemoryStream m = new MemoryStream(data))
                        using (BinaryReader reader = new BinaryReader(m))
                        {
                            CoreProtocolMessage.processHelloMessageV6(endpoint, reader);
                        }
                        break;

                    case ProtocolMessageCode.helloData:
                        using (MemoryStream m = new MemoryStream(data))
                        using (BinaryReader reader = new BinaryReader(m))
                        {
                            if (!CoreProtocolMessage.processHelloMessageV6(endpoint, reader))
                            {
                                Events.Emit("hello_rejected", new Dictionary<string, object?> { ["peer"] = endpoint.getFullAddress(true) });
                                return;
                            }
                            char node_type = endpoint.presenceAddress!.type;
                            ulong last_block_num = reader.ReadIxiVarUInt();
                            int bcLen = (int)reader.ReadIxiVarUInt();
                            reader.ReadBytes(bcLen);
                            endpoint.blockHeight = last_block_num;
                            reader.ReadIxiVarUInt();                    // block version
                            endpoint.helloReceived = true;
                            NetworkClientManager.recalculateLocalTimeDifference();

                            if (node_type == 'C')
                            {
                                // R/U NetworkProtocol.cs:105-112: every new connection to a bot friend sends getInfo.
                                Friend? f = FriendList.getFriend(endpoint.presence!.wallet);
                                if (f != null && f.bot)
                                {
                                    CoreStreamProcessor.sendGetBotInfo(f);
                                }
                                Events.Emit("connected", new Dictionary<string, object?>
                                {
                                    ["peer"] = endpoint.presence!.wallet.ToString(),
                                    ["nodeType"] = node_type.ToString(),
                                });
                                BotHelloCompleted?.Invoke(endpoint.presence!.wallet);
                            }
                        }
                        break;

                    case ProtocolMessageCode.s2data:
                        streamProcessor?.receiveData(data, endpoint);
                        break;

                    default:
                        // NO-OP: see the summary.
                        break;
                }
            }
            catch (Exception e)
            {
                // R wraps the switch the same way and logs (R NetworkProtocol.cs, outer catch).
                Logging.error("SimNode.parseProtocolMessage {0}: {1}", code, e);
                Events.Error("parseProtocolMessage:" + code, e);
            }
        }
    }
}

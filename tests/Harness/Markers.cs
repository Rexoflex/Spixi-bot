namespace Harness
{
    /// <summary>
    /// Failure markers (L5, L18). Each marker names one cause and is attached per case, only after the steps
    /// before it are proven. The CI self-test jobs break the bot (or the network) on purpose and require the
    /// matching marker for every case of the targeted scenario (.github/workflows/harness.yml, matrix `variant`).
    /// </summary>
    internal static class Markers
    {
        /// <summary>D-044: no block header from the testnet seeds in time. Infra, not a bot failure.</summary>
        public const string TestnetUnreachable = "INFRA-TESTNET-UNREACHABLE";

        /// <summary>Join handshake: the bot accepted the member but never answered getInfo (break `info`).</summary>
        public const string JoinInfoMissing = "JOIN-INFO-MISSING";

        /// <summary>Relay: the bot acked the post but the healthy receiver never got it (break `relay`, W8).</summary>
        public const string RelayMissing = "W8-RELAY-MISSING";

        /// <summary>Relay: the bot relayed the post but never sent msgReceived to the poster (break `ack`).</summary>
        public const string AckMissing = "ACK-MISSING";

        /// <summary>History: the bot replayed messages before a cursor it knows (break `cursor`).</summary>
        public const string CursorIgnored = "CURSOR-IGNORED";

        public static string Tag(string marker, string caseLabel) => $"{marker}[{caseLabel}]";
    }
}

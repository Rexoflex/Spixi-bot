namespace Harness
{
    /// <summary>
    /// Failure markers (L5, L18). Each marker names one cause and is attached per case, only after the steps
    /// before it are proven. The CI self-test jobs break the bot (or the network, or the client Core) on purpose and require the
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

        /// <summary>React: the bot got the reaction (its sender's next post was acked) but the healthy, linked
        /// author never got it relayed (break `reaction`).</summary>
        public const string ReactionMissing = "REACTION-MISSING";

        /// <summary>Delete: the author's delete reached the bot (ack) but the linked other member got no delete at all
        /// (break `delete`).</summary>
        public const string DeleteMissing = "DELETE-MISSING";

        /// <summary>Delete: the bot's delete arrived but the member's Core rejected it (break `delete-sign`: unsigned).</summary>
        public const string DeleteUnverified = "DELETE-UNVERIFIED";

        /// <summary>Delete: the bot reported the member as admin and processed its delete of another member's message,
        /// but the linked author got no delete (break `admin`). Not "ADMIN-DELETE-MISSING": DELETE-MISSING[case] would
        /// be a substring of it, and CI matches markers with Select-String -SimpleMatch (L18).</summary>
        public const string AdminDeleteNotRelayed = "ADMIN-DELETE-NOT-RELAYED";

        /// <summary>Info: the bot answered getInfo with an empty serverName (break `servername`).</summary>
        public const string InfoServerNameMissing = "INFO-SERVERNAME-MISSING";

        /// <summary>Unknown codes: the member's process died on an injected unknown code or action after the positive
        /// control passed (break-core `unknown`).</summary>
        public const string ClientCrashedOnUnknown = "CLIENT-CRASHED-ON-UNKNOWN";

        /// <summary>Leave: the bot acked the leave but still lists the member to a late joiner (break `leave`).</summary>
        public const string LeaveIgnored = "LEAVE-IGNORED";

        public static string Tag(string marker, string caseLabel) => $"{marker}[{caseLabel}]";
    }
}

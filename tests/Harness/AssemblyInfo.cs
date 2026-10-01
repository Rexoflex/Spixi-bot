using Xunit;

// Scenarios start real processes (a bot with a testnet header wait, several SimClients) and pick free ports
// (BotProcess.FreePortPair releases them before the bot binds). xUnit v3 runs test classes in parallel by
// default; run them one at a time so runs cannot collide on ports or CPU (pre-session review R1 item 4).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

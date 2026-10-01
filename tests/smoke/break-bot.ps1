<#
.SYNOPSIS
  Deliberate breaks for the harness self-tests (L5, L18; harness.md W8). Each break comments out ONE statement
  in the legacy bot, in the CI checkout only. The script fails if the statement is not found exactly once, so a
  drifted source cannot make a self-test pass by accident. A break with a Scope looks only in the text from the
  scope line up to the next "public static " (the statement occurs elsewhere too); the scope line itself must also
  occur exactly once. An Inline break replaces an expression (not a statement) by "" plus a comment.

  relay       onChat broadcast to every member          SpixiBot/Network/StreamProcessor.cs:418   → W8-RELAY-MISSING
  ack         msgReceived for a chat message           SpixiBot/Network/StreamProcessor.cs:76    → ACK-MISSING
  info        the answer to getInfo                    SpixiBot/Network/StreamProcessor.cs:433   → JOIN-INFO-MISSING
  cursor      botGetMessages honours the cursor        SpixiBot/Messages/Messages.cs:208          → CURSOR-IGNORED
              (replaced by "always -1", the unknown-cursor path: the whole channel is replayed)
  reaction    onMsgReaction relay to every member      SpixiBot/Network/StreamProcessor.cs:340   → REACTION-MISSING
  delete      onMsgDelete relay of the bot's delete    SpixiBot/Network/StreamProcessor.cs:326   → DELETE-MISSING
  delete-sign onMsgDelete signs the bot's delete       SpixiBot/Network/StreamProcessor.cs:322   → DELETE-UNVERIFIED
              (scope onMsgDelete: the statement occurs 4 times in the file)
  admin       isAdmin returns the group's admin flag   SpixiBot/Network/StreamProcessor.cs:357   → ADMIN-DELETE-NOT-RELAYED
              (replaced by "return false;")
  servername  sendInfo's serverName                    SpixiBot/Network/StreamProcessor.cs:589   → INFO-SERVERNAME-MISSING
              (inline: the getOption call becomes "")
  leave       onLeave for a leave message              SpixiBot/Network/StreamProcessor.cs:191   → LEAVE-IGNORED
#>
param(
  [Parameter(Mandatory = $true)][ValidateSet('relay', 'ack', 'info', 'cursor', 'reaction', 'delete', 'delete-sign', 'admin', 'servername', 'leave')][string]$Break,
  [Parameter(Mandatory = $true)][string]$BotRoot
)
$ErrorActionPreference = 'Stop'
$table = @{
  relay  = @{ File = 'Network/StreamProcessor.cs'; Needle = 'NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message);' }
  ack    = @{ File = 'Network/StreamProcessor.cs'; Needle = 'sendReceivedConfirmation(message.sender, message.id, channel, endpoint);' }
  info   = @{ File = 'Network/StreamProcessor.cs'; Needle = 'sendInfo(endpoint.presence.wallet);' }
  cursor = @{ File = 'Messages/Messages.cs'; Needle = 'last_msg_index = messages[channel].FindLastIndex(x => x.id.SequenceEqual(last_message_id));'; Replace = 'last_msg_index = -1;' }
  reaction = @{ File = 'Network/StreamProcessor.cs'; Needle = 'NetworkServer.forwardMessage(ProtocolMessageCode.s2data, reaction_msg.getBytes());' }
  delete = @{ File = 'Network/StreamProcessor.cs'; Needle = 'NetworkServer.forwardMessage(ProtocolMessageCode.s2data, message.getBytes());' }
  'delete-sign' = @{ File = 'Network/StreamProcessor.cs'; Needle = 'message.sign(IxianHandler.getWalletStorage().getPrimaryPrivateKey());'; Scope = 'public static void onMsgDelete(' }
  admin = @{ File = 'Network/StreamProcessor.cs'; Needle = 'return group.admin;'; Replace = 'return false;' }
  servername = @{ File = 'Network/StreamProcessor.cs'; Needle = 'Node.settings.getOption("serverName", "Bot")'; Inline = $true }
  leave = @{ File = 'Network/StreamProcessor.cs'; Needle = 'onLeave(message.sender);' }
}
$b = $table[$Break]
$file = Join-Path $BotRoot $b.File
$src = Get-Content -Raw -Path $file
# The region the needle must occur in exactly once: the whole file, or the scope (scope line → next "public static ").
$start = 0
$end = $src.Length
if ($b.Scope) {
  $scopeCount = ([regex]::Matches($src, [regex]::Escape($b.Scope))).Count
  if ($scopeCount -ne 1) { Write-Error "break-bot ${Break}: expected the scope '$($b.Scope)' exactly once in $file, found $scopeCount"; exit 1 }
  $start = $src.IndexOf($b.Scope, [StringComparison]::Ordinal)
  $next = $src.IndexOf('public static ', $start + $b.Scope.Length, [StringComparison]::Ordinal)
  if ($next -ge 0) { $end = $next }
}
$region = $src.Substring($start, $end - $start)
$count = ([regex]::Matches($region, [regex]::Escape($b.Needle))).Count
$where = if ($b.Scope) { "in the scope '$($b.Scope)' of $file" } else { "in $file" }
if ($count -ne 1) { Write-Error "break-bot ${Break}: expected the statement exactly once $where, found $count"; exit 1 }
if ($b.Inline) {
  $new = "`"`" /* harness self-test '$Break': was " + $b.Needle + " */"
} else {
  $replacement = if ($b.Replace) { $b.Replace + ' ' } else { '' }
  $new = "/* harness self-test '$Break' */ $replacement// " + $b.Needle
}
$region = $region.Replace($b.Needle, $new)
$src = $src.Substring(0, $start) + $region + $src.Substring($end)
Set-Content -Path $file -Value $src -NoNewline
Write-Host "break-bot: '$Break' applied to $file"

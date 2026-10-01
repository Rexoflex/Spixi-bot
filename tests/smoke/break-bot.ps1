<#
.SYNOPSIS
  Deliberate breaks for the harness self-tests (L5, L18; harness.md W8). Each break comments out ONE statement
  in the legacy bot, in the CI checkout only. The script fails if the statement is not found exactly once, so a
  drifted source cannot make a self-test pass by accident.

  relay   onChat broadcast to every member              SpixiBot/Network/StreamProcessor.cs:418   → W8-RELAY-MISSING
  ack     msgReceived for a chat message               SpixiBot/Network/StreamProcessor.cs:76    → ACK-MISSING
  info    the answer to getInfo                        SpixiBot/Network/StreamProcessor.cs:433   → JOIN-INFO-MISSING
  cursor  botGetMessages honours the cursor            SpixiBot/Messages/Messages.cs:208          → CURSOR-IGNORED
          (replaced by "always -1", the unknown-cursor path: the whole channel is replayed)
#>
param(
  [Parameter(Mandatory = $true)][ValidateSet('relay', 'ack', 'info', 'cursor')][string]$Break,
  [Parameter(Mandatory = $true)][string]$BotRoot
)
$ErrorActionPreference = 'Stop'
$table = @{
  relay  = @{ File = 'Network/StreamProcessor.cs'; Needle = 'NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message);' }
  ack    = @{ File = 'Network/StreamProcessor.cs'; Needle = 'sendReceivedConfirmation(message.sender, message.id, channel, endpoint);' }
  info   = @{ File = 'Network/StreamProcessor.cs'; Needle = 'sendInfo(endpoint.presence.wallet);' }
  cursor = @{ File = 'Messages/Messages.cs'; Needle = 'last_msg_index = messages[channel].FindLastIndex(x => x.id.SequenceEqual(last_message_id));'; Replace = 'last_msg_index = -1;' }
}
$b = $table[$Break]
$file = Join-Path $BotRoot $b.File
$src = Get-Content -Raw -Path $file
$count = ([regex]::Matches($src, [regex]::Escape($b.Needle))).Count
if ($count -ne 1) { Write-Error "break-bot ${Break}: expected the statement exactly once in $file, found $count"; exit 1 }
$replacement = if ($b.Replace) { $b.Replace + ' ' } else { '' }
$src = $src.Replace($b.Needle, "/* harness self-test '$Break' */ $replacement// " + $b.Needle)
Set-Content -Path $file -Value $src -NoNewline
Write-Host "break-bot: '$Break' applied to $file"

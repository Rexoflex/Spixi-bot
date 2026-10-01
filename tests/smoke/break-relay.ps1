<#
.SYNOPSIS
  W8 / L5 deliberate break for the harness self-test: removes the chat relay from the legacy bot.
  In onChat the bot stores the post and broadcasts the member's raw message to every connected client:
      NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message);   (SpixiBot/Network/StreamProcessor.cs:418)
  This script comments that one statement out in the CI checkout only. It fails if the statement is not
  found exactly once, so a drifted source cannot make the self-test pass by accident.
#>
param([Parameter(Mandatory = $true)][string]$File)
$ErrorActionPreference = 'Stop'
$needle = 'NetworkServer.forwardMessage(ProtocolMessageCode.s2data, raw_message);'
$src = Get-Content -Raw -Path $File
$count = ([regex]::Matches($src, [regex]::Escape($needle))).Count
if ($count -ne 1) { Write-Error "break-relay: expected the relay statement exactly once in $File, found $count"; exit 1 }
$src = $src.Replace($needle, '/* W8 harness self-test: relay removed */ // ' + $needle)
Set-Content -Path $File -Value $src -NoNewline
Write-Host "break-relay: relay statement removed from $File"

<#
.SYNOPSIS
  Deliberate CLIENT Core break for the harness self-test `unknown` (L5, L18). Patches the client Core k
  (097341a, Streaming/CoreStreamProcessor.cs) in the CI checkout only (SimClient compiles it in), so that an
  UNDEFINED code or action calls Environment.FailFast instead of being handled:

  unknown   receiveData's final `default:` (~:1589, before `catch (Exception e)` ~:1593): an undefined
            SpixiMessageCode → FailFast
            onBotAction after its switch (`return false;` ~:2735): an undefined SpixiBotActionCode → FailFast
            → CLIENT-CRASHED-ON-UNKNOWN (tests/Harness/UnknownCodesTests.cs)

  Defined codes and actions are untouched, so every other scenario behaves as before. Each anchor is a regex that
  accepts CRLF or LF (the Windows checkout has CRLF) and must match exactly once, so a drifted Core cannot make
  the self-test pass by accident.
#>
param(
  [Parameter(Mandatory = $true)][ValidateSet('unknown')][string]$Break,
  [Parameter(Mandatory = $true)][string]$CoreRoot
)
$ErrorActionPreference = 'Stop'
$file = Join-Path $CoreRoot 'Streaming/CoreStreamProcessor.cs'
$src = Get-Content -Raw -Path $file
$nl = if ($src.Contains("`r`n")) { "`r`n" } else { "`n" }
$fail = "Environment.FailFast(`"harness self-test '$Break'`");"
$patches = @(
  @{
    Name    = 'receiveData default'
    Pattern = '(?<head>\r?\n[ \t]*default:[ \t]*)(?<body>\r?\n[ \t]*return new ReceiveDataResponse\(spixi_message, message, friend, sender_address, group_sender_address\);\s*\}\s*\}\s*catch \(Exception e\)\s*\{\s*Logging\.error\("Exception occured in StreamProcessor\.receiveData)'
    Insert  = $nl + '                        if (!Enum.IsDefined(typeof(SpixiMessageCode), spixi_message.type)) { ' + $fail + " } // harness self-test '$Break'"
  }
  @{
    Name    = 'onBotAction fall-through'
    Pattern = '(?<head>case SpixiBotActionCode\.banUser:\s*return true;\s*break;\s*\})(?<body>\s*return false;\s*\}\s*protected void onGetPayment\()'
    Insert  = $nl + '            if (!Enum.IsDefined(typeof(SpixiBotActionCode), sba.action)) { ' + $fail + " } // harness self-test '$Break'"
  }
)
foreach ($p in $patches) {
  $m = [regex]::Matches($src, $p.Pattern)
  if ($m.Count -ne 1) { Write-Error "break-core ${Break}: expected the anchor '$($p.Name)' exactly once in $file, found $($m.Count)"; exit 1 }
  $at = $m[0].Groups['body'].Index
  $src = $src.Substring(0, $at) + $p.Insert + $src.Substring($at)
}
Set-Content -Path $file -Value $src -NoNewline
Write-Host "break-core: '$Break' applied to $file"

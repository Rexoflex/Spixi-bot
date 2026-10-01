<#
.SYNOPSIS
  Start smoke for the legacy bot (harness spike, session 3). Measures, does not fix.

  Starts SpixiBot from a fresh copy of its output folder (the bot looks for its DLLs and writes Data/,
  activity/ and ixian.log relative to the working folder: Program.cs:33-41, Config.cs:95, Node.cs:235),
  in testnet mode with an unreachable seed (F4 hypothesis: the bot serves clients with no DLT), free ports,
  and an API login from a test config file (W11). Records:
    - whether the process survives (risks: checkVCRedist registry call on Linux, Program.cs:201-205;
      Console.Clear with redirected stdout, Program.cs:142; stats-screen thread, StatsConsoleScreen.cs:42-62)
    - seconds until the wallet address is printed (keygen time, W11 / no wallet pool)
    - seconds until the stream port accepts TCP
    - the HTTP status of an authenticated API call (sb_getChannels; the fixture seeds channels this way)
  Exit 0 = alive, listening and API answered 200. Exit 1 otherwise. Logs go to -OutDir.
#>
param(
  [Parameter(Mandatory = $true)][string]$BotDir,
  [string]$OutDir = 'smoke-out',
  [int]$StartTimeoutSec = 180,
  [int]$StaySec = 30,
  # Windows: start the bot in its own console window (no redirect). Console.Clear() throws on a redirected
  # stdout (Program.cs:142, CI run 36840465376); the log file is read instead of stdout.
  [switch]$OwnConsole
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'annotate.ps1')

function Get-FreePort {
  $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
  $l.Start(); $p = $l.LocalEndpoint.Port; $l.Stop(); return $p
}
function Test-Tcp([int]$port) {
  $c = [System.Net.Sockets.TcpClient]::new()
  try { $c.Connect('127.0.0.1', $port); return $true } catch { return $false } finally { $c.Dispose() }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("spixibot-smoke-" + [guid]::NewGuid().ToString('N'))
Copy-Item -Recurse -Path (Resolve-Path $BotDir).Path -Destination $work

$botPort = Get-FreePort
$apiPort = Get-FreePort
$apiPass = [guid]::NewGuid().ToString('N')          # no ':' (Config.cs:207-211 splits on ':')
Set-Content -Path (Join-Path $work 'ixian.cfg') -Value "addApiUser = smoke:$apiPass"

$botArgs = @('SpixiBot.dll', '-t', '-n', '127.0.0.1:1', '-p', "$botPort", '-a', "$apiPort", '-i', '127.0.0.1',
          '--disableWebStart', '--walletPassword', 'smoke-test-wallet-pw')
$stdout = Join-Path $OutDir 'bot.stdout.txt'
$stderr = Join-Path $OutDir 'bot.stderr.txt'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
if ($OwnConsole) {
  $proc = Start-Process -FilePath 'dotnet' -ArgumentList $botArgs -WorkingDirectory $work -PassThru -WindowStyle Minimized
} else {
  $proc = Start-Process -FilePath 'dotnet' -ArgumentList $botArgs -WorkingDirectory $work `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -NoNewWindow
}
$logFile = Join-Path $work 'ixian.log'
function Test-Text([string]$path, [string]$pattern) {
  if (-not (Test-Path $path)) { return $false }
  try {
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite'); $sr = [System.IO.StreamReader]::new($fs)
    $t = $sr.ReadToEnd(); $sr.Dispose(); return $t -match $pattern
  } catch { return $false }
}

$tAddr = $null; $tListen = $null; $exitedAt = $null
while ($sw.Elapsed.TotalSeconds -lt $StartTimeoutSec) {
  if ($proc.HasExited) { $exitedAt = $sw.Elapsed.TotalSeconds; break }
  if ($null -eq $tAddr -and ((Test-Text $stdout 'Public Node Address') -or (Test-Text $logFile 'Public Node Address'))) {
    $tAddr = [math]::Round($sw.Elapsed.TotalSeconds, 1)
  }
  if ($null -eq $tListen -and (Test-Tcp $botPort)) { $tListen = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
  if ($null -ne $tListen) { break }
  Start-Sleep -Milliseconds 500
}

# Stay up to catch late crashes (stats screen redraws every 2 s, clears every ~12 s).
$apiStatus = 'not tried'
if ($null -ne $tListen -and -not $proc.HasExited) {
  $deadline = $sw.Elapsed.TotalSeconds + $StaySec
  while ($sw.Elapsed.TotalSeconds -lt $deadline -and -not $proc.HasExited) { Start-Sleep -Milliseconds 500 }
  if (-not $proc.HasExited) {
    $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("smoke:$apiPass"))
    try {
      $r = Invoke-WebRequest -Uri "http://localhost:$apiPort/sb_getChannels" -Headers @{ Authorization = "Basic $pair" } `
             -TimeoutSec 15 -SkipHttpErrorCheck
      $apiStatus = "$($r.StatusCode)"
      Set-Content -Path (Join-Path $OutDir 'api-sb_getChannels.json') -Value $r.Content
    } catch { $apiStatus = "error: $($_.Exception.Message)" }
  } else { $exitedAt = $sw.Elapsed.TotalSeconds }
}

$alive = -not $proc.HasExited
$exitCode = if ($proc.HasExited) { $proc.ExitCode } else { 'n/a (alive)' }
if ($alive) { Stop-Process -Id $proc.Id -Force; $proc.WaitForExit(10000) | Out-Null }

foreach ($f in @('ixian.log')) { $p = Join-Path $work $f; if (Test-Path $p) { Copy-Item $p $OutDir } }
Get-ChildItem -Path $work -Filter 'ixian*.log' -ErrorAction SilentlyContinue | Copy-Item -Destination $OutDir

$os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
$ok = $alive -and ($null -ne $tListen) -and ($apiStatus -eq '200')
$summary = @"
## Bot start smoke — $os
| Measure | Value |
|---|---|
| Result | $(if ($ok) { 'PASS' } else { 'FAIL' }) |
| Alive after listen + $StaySec s | $alive |
| Exited at (s) / exit code | $exitedAt / $exitCode |
| Wallet address printed at (s) | $tAddr |
| Stream port listening at (s) | $tListen |
| API sb_getChannels status | $apiStatus |
"@
Set-Content -Path (Join-Path $OutDir 'summary.md') -Value $summary
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary }
Write-Host $summary
$outTail = if (Test-Path $stdout) { (Get-Content $stdout -Tail 60) -join "`n" } else { '(no stdout)' }
$errTail = if (Test-Path $stderr) { (Get-Content $stderr -Tail 60) -join "`n" } else { '(no stderr)' }
Write-Host '--- stdout (last 60 lines) ---'; Write-Host $outTail
Write-Host '--- stderr (last 60 lines) ---'; Write-Host $errTail
Write-Annotation -Level notice -Title "smoke summary $os" -Message $summary -MaxChunks 1
if ($ok -and (Test-Path $logFile)) { Write-Annotation -Level notice -Title "smoke ixian.log $os" -Message ((Get-Content $logFile -Tail 40) -join "`n") -MaxChunks 1 }
if (-not $ok) {
  Write-Annotation -Level error -Title "smoke stderr $os" -Message $errTail -MaxChunks 2
  Write-Annotation -Level error -Title "smoke stdout $os" -Message $outTail -MaxChunks 2
  if (Test-Path $logFile) { Write-Annotation -Level warning -Title "smoke ixian.log $os" -Message ((Get-Content $logFile -Tail 80) -join "`n") -MaxChunks 3 }
}
if (-not $ok) { exit 1 }

<#
.SYNOPSIS
  CI-ONLY start patch for running the legacy bot on Linux (session 3 decision; measured in CI run 36840465376).
  On .NET (not Mono) onStart calls checkVCRedist() (SpixiBot/Program.cs:204), which reads the Windows registry
  (Program.cs:50). On Linux that throws PlatformNotSupportedException, unhandled, so the old bot cannot start
  on Linux at all. This script comments out that one call in the CI checkout. It touches startup only, no
  message path. It fails if the call is not found exactly once.
  The Windows harness job runs the unpatched bot in its own console window instead.
#>
param([Parameter(Mandatory = $true)][string]$File)
$ErrorActionPreference = 'Stop'
$needle = 'checkVCRedist();'
$src = Get-Content -Raw -Path $File
$count = ([regex]::Matches($src, [regex]::Escape($needle))).Count
if ($count -ne 1) { Write-Error "patch-linux-start: expected '$needle' exactly once in $File, found $count"; exit 1 }
$src = $src.Replace($needle, '/* CI-only Linux start patch: registry check skipped */ // ' + $needle)
Set-Content -Path $File -Value $src -NoNewline
Write-Host "patch-linux-start: checkVCRedist() call removed from $File"

<#
  GitHub Actions annotations. Annotations are readable through the public REST API
  (check-runs/{id}/annotations) without signing in, unlike job logs and artifacts. The session reads CI
  results that way (session 3), so every failure detail the harness needs is also emitted here.
  GitHub keeps at most 10 annotations per level per step; long text is split into chunks.
#>
function Write-Annotation {
  param([ValidateSet('notice', 'warning', 'error')][string]$Level, [string]$Title, [string]$Message, [int]$MaxChunks = 4)
  if (-not $env:GITHUB_ACTIONS) { return }
  $esc = { param($s) $s.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A') }
  $t = (& $esc $Title).Replace(':', '%3A').Replace(',', '%2C')
  $chunk = 3500
  $n = [math]::Min($MaxChunks, [math]::Ceiling([math]::Max(1, $Message.Length) / $chunk))
  # Keep the END of long text: the cause of a failure is usually last.
  $start = [math]::Max(0, $Message.Length - $n * $chunk)
  for ($i = 0; $i -lt $n; $i++) {
    $from = $start + $i * $chunk
    $len = [math]::Min($chunk, $Message.Length - $from)
    if ($len -le 0) { break }
    $part = $Message.Substring($from, $len)
    Write-Host ("::{0} title={1} ({2}/{3})::{4}" -f $Level, $t, ($i + 1), $n, (& $esc $part))
  }
}

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runDirectory = Join-Path $root ('.local\e2e\ownership-' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Force $runDirectory | Out-Null

function Test-Ownership($record, [string]$marker) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($record.Pid)" -ErrorAction SilentlyContinue
    if (!$process) { return 'process-not-found' }
    if ($process.Name -ne $record.Executable) { return 'executable-mismatch' }
    if ([string]::IsNullOrWhiteSpace($process.CommandLine) -or $process.CommandLine -notlike "*$marker*") { return 'command-marker-mismatch' }
    $started = (Get-Process -Id $record.Pid).StartTime.ToUniversalTime().ToString('o')
    if ($started -ne $record.StartedAtUtc) { return 'start-time-mismatch' }
    return 'exact-match'
}

$process = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList @('/d', '/c', "ping 127.0.0.1 -n 20 > `"$runDirectory\owned.log`"") -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Milliseconds 200
    $record = [pscustomobject]@{ Pid = $process.Id; StartedAtUtc = $process.StartTime.ToUniversalTime().ToString('o'); Executable = 'cmd.exe' }
    $startMismatch = [pscustomobject]@{ Pid = $record.Pid; StartedAtUtc = '2000-01-01T00:00:00.0000000Z'; Executable = 'cmd.exe' }
    $executableMismatch = [pscustomobject]@{ Pid = $record.Pid; StartedAtUtc = $record.StartedAtUtc; Executable = 'node.exe' }
    if ((Test-Ownership $startMismatch $runDirectory) -ne 'start-time-mismatch') { throw 'Start-time mismatch was not rejected.' }
    if ((Test-Ownership $executableMismatch $runDirectory) -ne 'executable-mismatch') { throw 'Executable mismatch was not rejected.' }
    if ((Test-Ownership $record (Join-Path $runDirectory 'other-run')) -ne 'command-marker-mismatch') { throw 'Command-marker mismatch was not rejected.' }
    if ((Test-Ownership $record $runDirectory) -ne 'exact-match') { throw 'Exact ownership was not recognized.' }
    if ($process.HasExited) { throw 'The isolated candidate exited unexpectedly.' }
    Write-Output 'Ownership mismatches rejected: start-time, executable, command marker. Candidate was not terminated.'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
}

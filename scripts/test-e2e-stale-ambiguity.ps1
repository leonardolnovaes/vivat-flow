[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runId = "ambiguous$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$directory = Join-Path $root ".local\e2e\$runId"
New-Item -ItemType Directory -Force $directory | Out-Null
$marker = Join-Path $directory 'candidate.log'
$candidate = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList @('/d', '/c', "ping 127.0.0.1 -n 20 > `"$marker`"") -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Milliseconds 200
    $manifest = [ordered]@{ SchemaVersion = 2; RunId = $runId; Lifecycle = 'running'; RunnerPid = 999999; RunnerStartedAtUtc = '2000-01-01T00:00:00.0000000Z'; ContainerName = "tsdt-e2e-postgres-$runId"; Resources = @{ candidate = @{ Pid = $candidate.Id; StartedAtUtc = '2000-01-01T00:00:00.0000000Z'; Executable = 'cmd.exe'; Command = "marker=$directory" } } }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'manifest.json') -Encoding utf8
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\run-e2e.ps1') -InfrastructureOnly
    if ($LASTEXITCODE -ne 0) { throw 'Fresh runner did not complete after scanning the ambiguous manifest.' }
    $after = Get-Content -Raw (Join-Path $directory 'manifest.json') | ConvertFrom-Json
    $same = Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue
    if (!$same) { throw 'Scanner terminated the ambiguous candidate.' }
    if ($after.Lifecycle -ne 'recovery-incomplete') { throw "Expected recovery-incomplete, found $($after.Lifecycle)." }
    $skipped = @($after.RecoveryResult.Skipped)
    if ($skipped.Count -ne 1 -or $skipped[0].Name -ne 'candidate' -or $skipped[0].Reason -ne 'start-time-mismatch') { throw 'Persisted skipped ownership reason is incorrect.' }
    if (@($after.RecoveryResult.Recovered) -contains 'candidate') { throw 'Ambiguous candidate was falsely recorded as recovered.' }
    Write-Output "Scanner ambiguity PASS runId=$runId candidatePid=$($candidate.Id) reason=$($skipped[0].Reason)"
}
finally {
    if (-not $candidate.HasExited) { Stop-Process -Id $candidate.Id -ErrorAction SilentlyContinue }
}

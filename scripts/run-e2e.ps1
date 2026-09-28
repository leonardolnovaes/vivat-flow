[CmdletBinding()]
param(
    [ValidateRange(1025, 65535)] [int]$PostgresPort = 55435,
    [ValidateRange(1025, 65535)] [int]$ApiPort = 57228,
    [ValidateRange(1025, 65535)] [int]$FrontendPort = 5174,
    [ValidateSet('None', 'AfterFrontendReady', 'AfterPlaywrightStarted')]
    [string]$FailureInjection = 'None',
    [switch]$InfrastructureOnly,
    [switch]$AbandonAfterFrontendReady,
    [ValidateRange(0, 300)] [int]$HoldAfterFrontendReadySeconds = 0,
    [switch]$KeepEnvironment,
    [switch]$ReuseEnvironment,
    [switch]$Cleanup,
    [string[]]$PlaywrightArgs = @()
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) { $PSNativeCommandUseErrorActionPreference = $false }
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$developmentDirectory = Join-Path $repositoryRoot '.local\e2e\development'
$isReusableMode = $KeepEnvironment -or $ReuseEnvironment
$modeCount = 0; if ($KeepEnvironment) { $modeCount++ }; if ($ReuseEnvironment) { $modeCount++ }; if ($Cleanup) { $modeCount++ }
if ($modeCount -gt 1) { throw 'Choose only one of -KeepEnvironment, -ReuseEnvironment, or -Cleanup.' }
if ($Cleanup -and ($InfrastructureOnly -or $AbandonAfterFrontendReady -or $HoldAfterFrontendReadySeconds -gt 0 -or $FailureInjection -ne 'None' -or $PlaywrightArgs.Count -gt 0)) { throw '-Cleanup cannot be combined with run options.' }
if ($ReuseEnvironment -and ($InfrastructureOnly -or $AbandonAfterFrontendReady -or $HoldAfterFrontendReadySeconds -gt 0 -or $FailureInjection -ne 'None')) { throw '-ReuseEnvironment cannot be combined with infrastructure or failure-injection options.' }
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$runDirectory = if ($isReusableMode -or $Cleanup) { $developmentDirectory } else { Join-Path $repositoryRoot ".local\e2e\$runId" }
$containerName = "tsdt-e2e-postgres-$runId"
$databaseName = "tsdt_e2e_$runId"
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$manifestPath = Join-Path $runDirectory 'manifest.json'
$runnerStartedAt = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
$runState = [ordered]@{ SchemaVersion = 2; RunId = $runId; Lifecycle = 'starting'; StartedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); RunnerPid = $PID; RunnerStartedAtUtc = $runnerStartedAt; ContainerName = $containerName; DatabaseName = $databaseName; Ports = @($PostgresPort, $ApiPort, $FrontendPort); Resources = @{} }
$owned = @{}
$acquiredResources = $false
$cleanupResult = [ordered]@{ FrontendStopped = $true; BackendStopped = $true; PlaywrightStopped = $true; PostgresRemoved = $true; DedicatedPortsReleased = $false }
$keepResources = $isReusableMode
$skipFinalCleanup = $false
$dbPassword = $null; $adminPassword = $null; $adminEmail = $null; $connection = $null

function New-Secret { $bytes = [byte[]]::new(24); $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create(); try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }; "E2e!aA1$([Convert]::ToBase64String($bytes).Replace('+','A').Replace('/','b').Replace('=',''))" }
function Log([string]$message) { $line = "$(Get-Date -Format o) $message"; Add-Content (Join-Path $runDirectory 'orchestrator.log') $line; Write-Host $line }
function Save-Manifest { $runState | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8 }
function Test-RecordedProcess($record, [string]$expectedRunDirectory) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($record.Pid)" -ErrorAction SilentlyContinue
    if (!$process) { return [pscustomobject]@{ Owned = $false; Reason = 'process-not-found' } }
    if ($process.Name -ne $record.Executable) { return [pscustomobject]@{ Owned = $false; Reason = 'executable-mismatch' } }
    if ([string]::IsNullOrWhiteSpace($process.CommandLine) -or $process.CommandLine -notlike "*$expectedRunDirectory*") { return [pscustomobject]@{ Owned = $false; Reason = 'command-marker-mismatch' } }
    $started = (Get-Process -Id $record.Pid -ErrorAction SilentlyContinue).StartTime.ToUniversalTime().ToString('o')
    if ($started -ne $record.StartedAtUtc) { return [pscustomobject]@{ Owned = $false; Reason = 'start-time-mismatch' } }
    return [pscustomobject]@{ Owned = $true; Reason = 'exact-match' }
}
function Recover-StaleRuns {
    $e2eRoot = Join-Path $repositoryRoot '.local\e2e'; if (!(Test-Path $e2eRoot)) { return }
    foreach ($directory in Get-ChildItem $e2eRoot -Directory) {
        if ($directory.FullName -eq $developmentDirectory) { continue }
        $path = Join-Path $directory.FullName 'manifest.json'; if (!(Test-Path $path)) { continue }
        try { $manifest = Get-Content -Raw $path | ConvertFrom-Json } catch { Write-Warning "Skipping malformed E2E manifest $path."; continue }
        if (!$manifest.SchemaVersion -or $manifest.SchemaVersion -ne 2) { Log "Legacy E2E manifest retained without process recovery: $path"; continue }
        if (!$manifest.RunId -or !$manifest.ContainerName -or !$manifest.Resources) { Write-Warning "Skipping incomplete current-schema manifest $path."; continue }
        if ($manifest.RunId -eq $runId) { continue }
        if ($manifest.Lifecycle -in @('completed', 'clean')) { continue }
        if ($manifest.Reusable -and $manifest.Lifecycle -eq 'reusable') { continue }
        if (!$manifest.RunnerPid -or !$manifest.RunnerStartedAtUtc) { Write-Warning "Skipping current-schema manifest without runner identity: $path"; continue }
        $runner = Get-Process -Id $manifest.RunnerPid -ErrorAction SilentlyContinue
        if ($runner -and $runner.StartTime.ToUniversalTime().ToString('o') -eq $manifest.RunnerStartedAtUtc) { throw "An active owned E2E run ($($manifest.RunId)) exists. Parallel runs are not supported." }
        $recovered = @(); $skipped = @()
        foreach ($property in $manifest.Resources.PSObject.Properties) {
            if ($property.Name -eq 'postgres' -or !$property.Value.Pid) { continue }
            $record = $property.Value
            $decision = Test-RecordedProcess $record $directory.FullName
            if ($decision.Owned) { taskkill.exe /PID $record.Pid /T /F *> $null; $recovered += $property.Name } else { $skipped += @{ Name = $property.Name; Reason = $decision.Reason } }
        }
        try { $labels = docker inspect --format '{{json .Config.Labels}}' $manifest.ContainerName 2>$null } catch { $labels = $null }
        if ($labels -and $LASTEXITCODE -eq 0) { $parsed = $labels | ConvertFrom-Json; if ($parsed.'tsdt.e2e' -ne 'true' -or $parsed.'tsdt.e2e.run' -ne $manifest.RunId) { throw "Ambiguous stale container for run $($manifest.RunId); refusing removal." }; docker rm --force $manifest.ContainerName *> $null; $recovered += 'postgres' }
        $manifest.Lifecycle = if ($skipped.Count -gt 0) { 'recovery-incomplete' } else { 'clean' }; $manifest | Add-Member -NotePropertyName RecoveredAtUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force; $manifest | Add-Member -NotePropertyName RecoveryResult -NotePropertyValue @{ Recovered = $recovered; Skipped = $skipped } -Force; $manifest | ConvertTo-Json -Depth 6 | Set-Content $path -Encoding utf8
    }
}
function Assert-FreePort([int]$port) { if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { throw "Port $port is already in use. Refusing to share an E2E port." } }
function Topology([string]$name, [int]$rootPid) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine; $pending = [Collections.Generic.Queue[int]]::new(); $seen = [Collections.Generic.HashSet[int]]::new(); $result = @(); $pending.Enqueue($rootPid)
    while ($pending.Count) { $currentPid = $pending.Dequeue(); if (-not $seen.Add($currentPid)) { continue }; $item = $all | Where-Object ProcessId -eq $currentPid; if ($item) { $result += $item; $all | Where-Object ParentProcessId -eq $currentPid | ForEach-Object { $pending.Enqueue([int]$_.ProcessId) } } }
    $result | ConvertTo-Json | Set-Content (Join-Path $runDirectory "$name-topology.json") -Encoding utf8
}
function Start-Owned([string]$name, [string]$command, [hashtable]$environment, [string]$workingDirectory) {
    $info = [Diagnostics.ProcessStartInfo]::new(); $info.FileName = "$env:SystemRoot\System32\cmd.exe"; $info.Arguments = "/d /s /c `"$command`""; $info.WorkingDirectory = $workingDirectory; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    foreach ($key in $environment.Keys) { $info.Environment[$key] = $environment[$key] }
    $process = [Diagnostics.Process]::Start($info); if (!$process) { throw "Could not start $name." }; $owned[$name] = $process; $record = @{ Pid = $process.Id; StartedAtUtc = $process.StartTime.ToUniversalTime().ToString('o'); Executable = 'cmd.exe'; Command = $command }
    if ($runState.Resources -is [hashtable]) { $runState.Resources[$name] = $record } else { $runState.Resources | Add-Member -NotePropertyName $name -NotePropertyValue $record -Force }
    Save-Manifest; Log "$name root PID=$($process.Id), executable=cmd.exe"; Start-Sleep -Milliseconds 300; Topology "$name-start" $process.Id; $process
}
function Stop-Owned([string]$name) {
    $process = $owned[$name]; if (!$process) { return $true }; Topology "$name-before-cleanup" $process.Id
    $alive = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($alive) { taskkill.exe /PID $process.Id /T /F *> $null; Start-Sleep -Milliseconds 200 }
    Topology "$name-after-cleanup" $process.Id
    return $null -eq (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
}
function Wait-Url([string]$uri, [string]$name, [switch]$insecure) {
    for ($attempt=1; $attempt -le 45; $attempt++) { $args=@('--silent','--output','NUL','--write-out','%{http_code}','--max-time','2'); if ($insecure) { $args += '--insecure' }; $args += $uri; try { $status=(& curl.exe @args 2>$null).Trim() } catch { $status='' }; if ($LASTEXITCODE -eq 0 -and $status -eq '200') { Log "$name ready"; return }; Start-Sleep -Seconds 1 }; throw "$name did not become healthy. See $runDirectory."
}
function Get-ProductionSourceFingerprint {
    $files = @(git -C $repositoryRoot ls-files --cached --others --exclude-standard -- backend/src frontend/src frontend/vite.config.ts | ForEach-Object { Join-Path $repositoryRoot $_ } | Where-Object { (Test-Path $_) -and $_ -notmatch '[\\/](bin|obj)[\\/]' -and [IO.Path]::GetExtension($_) -in @('.cs', '.csproj', '.ts', '.tsx', '.css', '.json') })
    $builder = [Text.StringBuilder]::new()
    foreach ($file in $files | Sort-Object) { [void]$builder.Append($file).Append(':').Append((Get-FileHash $file -Algorithm SHA256).Hash).Append("`n") }
    $sha = [Security.Cryptography.SHA256]::Create(); try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($builder.ToString()))).Replace('-', '')) } finally { $sha.Dispose() }
}
function Unprotect-Secret([string]$protectedValue) {
    $secret = ConvertTo-SecureString $protectedValue
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Get-ReusableManifest {
    if (!(Test-Path $manifestPath)) { throw "No reusable E2E environment exists at $runDirectory. Start one with -KeepEnvironment." }
    try { $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json } catch { throw "The reusable E2E manifest is unreadable: $manifestPath" }
    if (!$manifest.Reusable -or $manifest.Lifecycle -notin @('reusable', 'cleaning') -or !$manifest.RunId -or !$manifest.ContainerName -or !$manifest.Resources) { throw "The reusable E2E manifest is not in a reusable state. Run -Cleanup before provisioning a new environment." }
    return $manifest
}
function Stop-RecordedProcess($record, [string]$expectedRunDirectory) {
    $decision = Test-RecordedProcess $record $expectedRunDirectory
    if ($decision.Reason -eq 'process-not-found') { return }
    if (!$decision.Owned) { throw "Refusing to stop an unverified E2E process: $($decision.Reason)." }
    taskkill.exe /PID $record.Pid /T /F *> $null
    if (Get-Process -Id $record.Pid -ErrorAction SilentlyContinue) { throw "Could not stop owned E2E process $($record.Pid)." }
}
function Remove-ReusableEnvironment($manifest) {
    foreach ($name in @('frontend', 'backend')) { if ($manifest.Resources.$name) { Stop-RecordedProcess $manifest.Resources.$name $runDirectory } }
    $labels = docker inspect --format '{{json .Config.Labels}}' $manifest.ContainerName 2>$null
    if ($LASTEXITCODE -eq 0 -and $labels) {
        $parsed = $labels | ConvertFrom-Json
        if ($parsed.'tsdt.e2e' -ne 'true' -or $parsed.'tsdt.e2e.run' -ne $manifest.RunId) { throw 'Reusable E2E PostgreSQL ownership verification failed.' }
        docker rm --force $manifest.ContainerName *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the owned reusable E2E PostgreSQL container.' }
    }
    $manifest.Lifecycle = 'clean'; $manifest | Add-Member -NotePropertyName CompletedAtUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force; $manifest | ConvertTo-Json -Depth 6 | Set-Content $manifestPath -Encoding utf8
}
function Assert-ReusableEnvironment($manifest) {
    if ($manifest.DatabaseName -notmatch '^tsdt_e2e_[a-f0-9]{12}$') { throw 'Reusable E2E database identity is invalid.' }
    foreach ($name in @('backend', 'frontend')) { if (!$manifest.Resources.$name -or !(Test-RecordedProcess $manifest.Resources.$name $runDirectory).Owned) { throw "Reusable E2E $name process is stale or does not match its ownership record. Run -Cleanup, then -KeepEnvironment." } }
    $labels = docker inspect --format '{{json .Config.Labels}}' $manifest.ContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or !$labels) { throw 'Reusable E2E PostgreSQL container is unavailable. Run -Cleanup, then -KeepEnvironment.' }
    $parsed = $labels | ConvertFrom-Json
    if ($parsed.'tsdt.e2e' -ne 'true' -or $parsed.'tsdt.e2e.run' -ne $manifest.RunId) { throw 'Reusable E2E PostgreSQL ownership verification failed.' }
    docker exec $manifest.ContainerName pg_isready -U tsdt_e2e -d $manifest.DatabaseName *> $null; if ($LASTEXITCODE -ne 0) { throw 'Reusable E2E PostgreSQL is unhealthy.' }
    Wait-Url "https://localhost:$($manifest.Ports[1])/health" Backend -insecure; Wait-Url "http://127.0.0.1:$($manifest.Ports[2])/health" Frontend
    if ($manifest.ProductionSourceFingerprint -ne (Get-ProductionSourceFingerprint)) { throw 'Production source changed since this reusable E2E environment was provisioned. Run -Cleanup, then -KeepEnvironment to avoid testing stale binaries.' }
}

$exitCode = 1
try {
    if ($Cleanup) {
        $runState = Get-ReusableManifest; $runId = $runState.RunId; $containerName = $runState.ContainerName; $databaseName = $runState.DatabaseName; $PostgresPort = $runState.Ports[0]; $ApiPort = $runState.Ports[1]; $FrontendPort = $runState.Ports[2]
        Write-Host 'E2E mode: CLEANUP'; Remove-ReusableEnvironment $runState; Write-Host "Reusable E2E environment cleaned: $runDirectory"; $skipFinalCleanup = $true; $exitCode = 0; return
    }
    if ($ReuseEnvironment) {
        $runState = Get-ReusableManifest; $runId = $runState.RunId; $containerName = $runState.ContainerName; $databaseName = $runState.DatabaseName; $PostgresPort = $runState.Ports[0]; $ApiPort = $runState.Ports[1]; $FrontendPort = $runState.Ports[2]
        $adminEmail = $runState.AdminEmail; $adminPassword = Unprotect-Secret $runState.AdminPasswordProtected
        if ([string]::IsNullOrWhiteSpace($adminEmail) -or [string]::IsNullOrWhiteSpace($adminPassword)) { throw 'Reusable E2E credentials are unavailable to this Windows user. Run -Cleanup, then -KeepEnvironment.' }
        Write-Host "E2E mode: REUSE (run $runId; provisioning skipped)"; Assert-ReusableEnvironment $runState
    }
    else {
        New-Item -ItemType Directory -Force $runDirectory | Out-Null; $runState.Reusable = [bool]$KeepEnvironment; Save-Manifest; Write-Host "E2E mode: $(if ($KeepEnvironment) { 'DEVELOPMENT/KEEP' } else { 'ISOLATED' })"; Log "Starting isolated E2E run $runId"
        docker version *> $null; if ($LASTEXITCODE -ne 0) { throw 'Docker is unavailable.' }; if (!$KeepEnvironment) { Recover-StaleRuns }; Assert-FreePort $PostgresPort; Assert-FreePort $ApiPort; Assert-FreePort $FrontendPort; Log 'Dedicated E2E ports are available.'
        if ((docker ps -a --filter "name=^/$containerName$" --format '{{.Names}}') -eq $containerName) { throw 'A container with this exact run identity already exists.' }
        $dbPassword=New-Secret; $adminPassword=New-Secret; $adminEmail="e2e-admin-$runId@example.test"; $connection="Host=127.0.0.1;Port=$PostgresPort;Database=$databaseName;Username=tsdt_e2e;Password=$dbPassword"
        if ($connection -notmatch "Database=$databaseName" -or $connection -match '(?i)(production|staging|database=tsdt[;\s])') { throw 'E2E connection safety assertion failed.' }
        if ($KeepEnvironment) { $runState.AdminEmail=$adminEmail; $runState.AdminPasswordProtected=(ConvertTo-SecureString $adminPassword -AsPlainText -Force | ConvertFrom-SecureString); $runState.ProductionSourceFingerprint=Get-ProductionSourceFingerprint; Save-Manifest; Log 'Reusable E2E source fingerprint recorded.' }
        docker run --detach --name $containerName --label tsdt.e2e=true --label "tsdt.e2e.run=$runId" --publish "127.0.0.1:$PostgresPort`:5432" --env "POSTGRES_DB=$databaseName" --env POSTGRES_USER=tsdt_e2e --env "POSTGRES_PASSWORD=$dbPassword" postgres:18-alpine *> $null; if ($LASTEXITCODE -ne 0) { throw 'Could not start E2E PostgreSQL.' }; $acquiredResources = $true; $runState.Resources['postgres'] = @{ Name = $containerName; Labels = @{ 'tsdt.e2e' = 'true'; 'tsdt.e2e.run' = $runId } }; Save-Manifest
        for ($attempt=1; $attempt -le 30; $attempt++) { docker exec $containerName pg_isready -U tsdt_e2e -d $databaseName *> $null; if ($LASTEXITCODE -eq 0) { break }; if ($attempt -eq 30) { throw 'E2E PostgreSQL did not become ready.' }; Start-Sleep -Seconds 1 }; $runState.Lifecycle = 'running'; Save-Manifest; Log "PostgreSQL container ready: $containerName"
        dotnet build "$repositoryRoot\backend\Tsdt.sln" --configuration Release --no-restore '-p:BaseOutputPath=bin/E2E/'; if ($LASTEXITCODE -ne 0) { throw 'Release build failed. If assets are missing, run .\scripts\restore.ps1 once.' }
        $apiLog=Join-Path $runDirectory 'backend.log'; $api=Start-Owned backend "dotnet `"$repositoryRoot\backend\src\Tsdt.Api\bin\E2E\Release\net10.0\Tsdt.Api.dll`" --urls https://localhost:$ApiPort > `"$apiLog`" 2>&1" @{ ASPNETCORE_ENVIRONMENT='Development'; ConnectionStrings__DefaultConnection=$connection; BootstrapAdmin__Email=$adminEmail; BootstrapAdmin__FullName='E2E Administrator'; BootstrapAdmin__Password=$adminPassword } $repositoryRoot; Wait-Url "https://localhost:$ApiPort/health" Backend -insecure
        $nodePath = (Get-Command node.exe -ErrorAction Stop).Source; $viteCli = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'; $playwrightCli = Join-Path $frontendDirectory 'node_modules\@playwright\test\cli.js'; if (!(Test-Path $viteCli) -or !(Test-Path $playwrightCli)) { throw 'Project-local Vite or Playwright CLI is missing. Run npm install in frontend.' }
        $frontendLog=Join-Path $runDirectory 'frontend.log'; $frontend=Start-Owned frontend "`"$nodePath`" `"$viteCli`" --port $FrontendPort > `"$frontendLog`" 2>&1" @{ VITE_API_PROXY_TARGET="https://localhost:$ApiPort" } $frontendDirectory; Wait-Url "http://127.0.0.1:$FrontendPort/health" Frontend
    }
    if ($AbandonAfterFrontendReady) { Log 'Runner deliberately abandoning host after frontend readiness.'; Stop-Process -Id $PID -Force }
    if ($HoldAfterFrontendReadySeconds -gt 0) { Log "Holding active run for $HoldAfterFrontendReadySeconds seconds."; Start-Sleep -Seconds $HoldAfterFrontendReadySeconds }
    if ($FailureInjection -eq 'AfterFrontendReady') { throw 'Injected failure after frontend readiness.' }
    if ($InfrastructureOnly) { Log 'Infrastructure-only cycle complete.'; $exitCode = 0; return }
    $nodePath = if ($nodePath) { $nodePath } else { (Get-Command node.exe -ErrorAction Stop).Source }; $playwrightCli = if ($playwrightCli) { $playwrightCli } else { Join-Path $frontendDirectory 'node_modules\@playwright\test\cli.js' }
    $escapedPlaywrightArgs = @($PlaywrightArgs | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $playwrightLog=Join-Path $runDirectory 'playwright.log'; $argumentText="test $escapedPlaywrightArgs"; $playwright=Start-Owned playwright "`"$nodePath`" `"$playwrightCli`" $argumentText > `"$playwrightLog`" 2>&1" @{ PLAYWRIGHT_BASE_URL="http://127.0.0.1:$FrontendPort"; E2E_ADMIN_EMAIL=$adminEmail; E2E_ADMIN_PASSWORD=$adminPassword; E2E_AUTH_DIR=(Join-Path $runDirectory 'auth'); E2E_RUN_ID=$runId; E2E_REUSE_AUTH=if ($ReuseEnvironment) { 'true' } else { 'false' } } $frontendDirectory; if ($FailureInjection -eq 'AfterPlaywrightStarted') { throw 'Injected failure after Playwright startup.' }; $playwright.WaitForExit(); Topology 'playwright-finished' $playwright.Id; if ($playwright.ExitCode -ne 0) { throw "Playwright failed. See $playwrightLog." }; $exitCode = 0
}
catch {
    if (Test-Path $runDirectory) { Log "Run failed: $($_.Exception.Message)" }
    Write-Error $_
}
finally {
    if ($skipFinalCleanup) { exit $exitCode }
    if ($ReuseEnvironment -or ($KeepEnvironment -and $acquiredResources)) {
        try { $cleanupResult.PlaywrightStopped = Stop-Owned playwright } catch { Write-Warning $_ }
        $runState.Lifecycle = 'reusable'; $runState | Add-Member -NotePropertyName LastUsedAtUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force; Save-Manifest
        Write-Host "Reusable E2E environment retained: $runDirectory"
        exit $exitCode
    }
    $runState.Lifecycle = 'cleaning'; Save-Manifest
    try { $cleanupResult.PlaywrightStopped=Stop-Owned playwright } catch { Write-Warning $_ }; try { $cleanupResult.FrontendStopped=Stop-Owned frontend } catch { Write-Warning $_ }; try { $cleanupResult.BackendStopped=Stop-Owned backend } catch { Write-Warning $_ }
    try {
        if ($containerName -match '^tsdt-e2e-postgres-[a-f0-9]{12}$') {
            $labels = (docker inspect --format '{{json .Config.Labels}}' $containerName 2>$null | ConvertFrom-Json)
            if ($labels.'tsdt.e2e' -eq 'true' -and $labels.'tsdt.e2e.run' -eq $runId) { docker rm --force $containerName *> $null; $cleanupResult.PostgresRemoved=($LASTEXITCODE -eq 0) }
        }
    } catch { Write-Warning "Could not remove owned PostgreSQL container: $($_.Exception.Message)" }
    $cleanupResult.DedicatedPortsReleased=-not (Get-NetTCPConnection -State Listen -LocalPort $PostgresPort,$ApiPort,$FrontendPort -ErrorAction SilentlyContinue); $cleanupResult | ConvertTo-Json | Set-Content (Join-Path $runDirectory 'cleanup.json') -Encoding utf8
    if ($cleanupResult.FrontendStopped -and $cleanupResult.BackendStopped -and $cleanupResult.PlaywrightStopped -and $cleanupResult.PostgresRemoved -and $cleanupResult.DedicatedPortsReleased) { $runState.Lifecycle = 'clean'; $runState.CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); Save-Manifest }
    Write-Host "E2E cleanup: frontend=$($cleanupResult.FrontendStopped); backend=$($cleanupResult.BackendStopped); playwright=$($cleanupResult.PlaywrightStopped); postgres=$($cleanupResult.PostgresRemoved); portsReleased=$($cleanupResult.DedicatedPortsReleased); logs=$runDirectory"
    if ($acquiredResources -and -not ($cleanupResult.FrontendStopped -and $cleanupResult.BackendStopped -and $cleanupResult.PlaywrightStopped -and $cleanupResult.PostgresRemoved -and $cleanupResult.DedicatedPortsReleased)) { $exitCode = 1; Write-Error 'E2E cleanup was incomplete.' }
}
exit $exitCode

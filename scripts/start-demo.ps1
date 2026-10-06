[CmdletBinding()]
param(
    [string]$Ref = 'main',
    [switch]$Restart,
    [switch]$Stop,
    [switch]$BackendOnly,
    [switch]$FrontendOnly
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) { $PSNativeCommandUseErrorActionPreference = $false }
if ($BackendOnly -and $FrontendOnly) { throw 'Choose at most one of -BackendOnly and -FrontendOnly.' }
if ($Stop -and ($Restart -or $BackendOnly -or $FrontendOnly)) { throw '-Stop cannot be combined with -Restart, -BackendOnly, or -FrontendOnly.' }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$demoRoot = Join-Path $repositoryRoot '.local\demo'
$demoWorktree = Join-Path $demoRoot 'worktree'
$demoLogDirectory = Join-Path $repositoryRoot '.local\logs\demo'
$deployedShaFile = Join-Path $demoRoot 'deployed-sha.txt'
$backendProjectRelativePath = 'backend\src\Tsdt.Api\Tsdt.Api.csproj'
$frontendRelativePath = 'frontend'
$sharedPostgresComposeFile = Join-Path $repositoryRoot 'infrastructure\docker-compose.yml'
$frontendPort = 5173
$backendHttpsPort = 7226
$postgresPort = 5432
$databaseName = 'tsdt'
$bootstrapSettingNames = @(
    'BootstrapAdmin__Email', 'BootstrapAdmin__FullName', 'BootstrapAdmin__Password',
    'PlatformBootstrapAdmin__Email', 'PlatformBootstrapAdmin__FullName', 'PlatformBootstrapAdmin__Password',
    'OrganizationBootstrap__Name', 'OrganizationBootstrap__Slug'
)

function Get-PortOwner([int]$Port) {
    try { $connection = Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq $Port | Select-Object -First 1 }
    catch { throw "The listener on port $Port could not be inspected. Refusing to start another process. $($_.Exception.Message)" }
    if ($null -eq $connection) { return $null }
    try { return Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction Stop }
    catch { throw "Port $Port is listening, but its owner could not be inspected. Refusing to start another process. $($_.Exception.Message)" }
}

function Get-HttpStatus([string]$Uri, [switch]$Insecure) {
    $arguments = @('--silent', '--show-error', '--output', 'NUL', '--write-out', '%{http_code}', '--max-time', '5')
    if ($Insecure) { $arguments += '--insecure' }
    $arguments += $Uri
    try { $status = (& curl.exe @arguments 2>$null).Trim() } catch { return $null }
    if ($LASTEXITCODE -ne 0) { return $null }
    return $status
}

function Test-Healthy([ValidateSet('Backend', 'Frontend')] [string]$Kind) {
    if ($Kind -eq 'Backend') { return (Get-HttpStatus "https://localhost:$backendHttpsPort/health" -Insecure) -eq '200' }
    return (Get-HttpStatus "http://127.0.0.1:$frontendPort/health") -eq '200'
}

function Test-DemoProjectOwner($Process, [ValidateSet('Backend', 'Frontend')] [string]$Kind) {
    $commandLine = [string]$Process.CommandLine
    if ($Kind -eq 'Backend') {
        $backendProject = Join-Path $demoWorktree $backendProjectRelativePath
        $backendExecutable = Join-Path $demoWorktree 'backend\src\Tsdt.Api\bin\Debug\net10.0\Tsdt.Api.exe'
        return ($Process.Name -eq 'dotnet.exe' -and $commandLine.Contains($backendProject) -and $commandLine.Contains("https://localhost:$backendHttpsPort")) -or
            ($Process.Name -eq 'Tsdt.Api.exe' -and $Process.ExecutablePath -eq $backendExecutable)
    }
    $frontendDirectory = Join-Path $demoWorktree $frontendRelativePath
    return $Process.Name -eq 'node.exe' -and $commandLine.Contains($frontendDirectory) -and $commandLine -match '(?i)[\\/]vite[\\/]bin[\\/]vite\.js' -and $commandLine -match "--port\s+$frontendPort"
}

function Wait-ForPortRelease([int]$Port) {
    for ($attempt = 1; $attempt -le 30; $attempt++) { if ($null -eq (Get-PortOwner $Port)) { return }; Start-Sleep -Seconds 1 }
    throw "Port $Port was not released within 30 seconds."
}

function Stop-DemoProcess($Process, [int]$Port, [string]$Name) {
    Write-Host "Stopping the DEMO $Name process (PID $($Process.ProcessId))."
    if (Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue) {
        $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId) -NoNewWindow -Wait -PassThru
        if ($termination.ExitCode -ne 0) {
            $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId, '/F') -NoNewWindow -Wait -PassThru
            if ($termination.ExitCode -ne 0) { throw "Could not stop the DEMO $Name process (PID $($Process.ProcessId))." }
        }
    }
    Wait-ForPortRelease $Port
}

function Read-LocalEnvironment {
    $environmentFile = Join-Path $repositoryRoot '.env'
    if (-not (Test-Path $environmentFile)) { throw '.env is unavailable. Configure the existing DEMO PostgreSQL credentials before starting DEMO.' }
    $values = @{}
    Get-Content $environmentFile | ForEach-Object { if ($_ -match '^\s*([^#=]+)=(.*)$') { $values[$matches[1].Trim()] = $matches[2].Trim() } }
    foreach ($name in @('POSTGRES_USER', 'POSTGRES_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace($values[$name])) { throw ".env is missing $name; cannot connect to the persistent DEMO database." }
    }
    return $values
}

function Start-SharedPostgres {
    if (-not (Test-Path $sharedPostgresComposeFile)) { throw "Shared PostgreSQL Compose file is missing: $sharedPostgresComposeFile" }
    & docker compose --file $sharedPostgresComposeFile up --detach --no-recreate postgres
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the existing shared PostgreSQL service for DEMO.' }
}

function Get-PostgresContainerId {
    $containerId = (@(& docker compose --file $sharedPostgresComposeFile ps --quiet postgres) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) { throw 'The shared PostgreSQL service is not running.' }
    return $containerId
}

function Invoke-DemoPsql([string]$ContainerId, [hashtable]$LocalValues, [string]$Database, [string]$Query) {
    $result = @($Query | & docker exec -i $ContainerId psql -U $LocalValues['POSTGRES_USER'] -d $Database -t -A)
    if ($LASTEXITCODE -ne 0) { throw "Could not query the persistent DEMO database '$Database'." }
    return @($result | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Confirm-DemoDatabase([string]$ContainerId, [hashtable]$LocalValues) {
    $exists = Invoke-DemoPsql $ContainerId $LocalValues 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$databaseName';"
    if ($exists -notcontains '1') { throw "The persistent DEMO database '$databaseName' does not exist. Refusing to create, seed, or replace it." }
}

function Get-Commit([string]$Revision) {
    $resolvedRevision = $Revision
    if ($Revision -eq 'main') {
        & git -C $repositoryRoot fetch --quiet origin '+refs/heads/main:refs/remotes/origin/main'
        if ($LASTEXITCODE -ne 0) { throw 'Could not refresh origin/main for the canonical DEMO deployment.' }
        $resolvedRevision = 'origin/main'
    }
    $commit = (@(& git -C $repositoryRoot rev-parse --verify "$resolvedRevision^{commit}") -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw "Could not resolve '$Revision' to a commit." }
    return $commit
}

function Get-DemoWorktreeCommit {
    if (-not (Test-Path $demoWorktree)) { return $null }
    $worktreeGitDirectory = Join-Path $demoWorktree '.git'
    if (-not (Test-Path $worktreeGitDirectory)) { throw "DEMO worktree path exists but is not a Git worktree: $demoWorktree" }
    $commit = (@(& git -C $demoWorktree rev-parse HEAD) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Could not determine the current DEMO worktree commit.' }
    return $commit
}

function Ensure-DemoWorktree([string]$Commit) {
    New-Item -ItemType Directory -Path $demoRoot -Force | Out-Null
    if (-not (Test-Path $demoWorktree)) {
        & git -C $repositoryRoot worktree add --detach $demoWorktree $Commit
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the persistent DEMO worktree.' }
        return
    }
    $worktreeGitDirectory = Join-Path $demoWorktree '.git'
    if (-not (Test-Path $worktreeGitDirectory)) { throw "DEMO worktree path exists but is not a Git worktree: $demoWorktree" }
    $changes = @(& git -C $demoWorktree status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -gt 0) { throw 'The DEMO worktree has tracked changes. Refusing to overwrite the deployed snapshot.' }
    $currentCommit = (@(& git -C $demoWorktree rev-parse HEAD) -join '').Trim()
    if ($currentCommit -ne $Commit) {
        & git -C $demoWorktree checkout --detach $Commit
        if ($LASTEXITCODE -ne 0) { throw 'Could not update the persistent DEMO worktree to the requested commit.' }
    }
}

function Get-PendingMigrations([string]$ContainerId, [hashtable]$LocalValues) {
    $migrationDirectory = Join-Path $demoWorktree 'backend\src\Tsdt.Api\Identity\Migrations'
    if (-not (Test-Path $migrationDirectory)) { throw "Migration directory is missing from the DEMO worktree: $migrationDirectory" }
    $expected = @(Get-ChildItem -Path $migrationDirectory -Filter '*.cs' | Where-Object Name -match '^\d+_.+\.cs$' | Where-Object Name -notmatch '\.Designer\.cs$' | ForEach-Object BaseName)
    $applied = @(Invoke-DemoPsql $ContainerId $LocalValues $databaseName 'SELECT "MigrationId" FROM "__EFMigrationsHistory";')
    return @($expected | Where-Object { $_ -notin $applied })
}

function Backup-DemoDatabase([string]$ContainerId, [hashtable]$LocalValues, [string[]]$PendingMigrations) {
    $backupDirectory = Join-Path $demoRoot 'backups'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupFile = Join-Path $backupDirectory "$databaseName-$stamp-before-migrations.dump"
    $containerFile = "/tmp/$databaseName-$stamp.dump"
    & docker exec $ContainerId pg_dump -U $LocalValues['POSTGRES_USER'] -Fc -d $databaseName -f $containerFile
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the DEMO database backup before pending migrations.' }
    try {
        & docker cp "${ContainerId}:$containerFile" $backupFile
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $backupFile)) { throw 'Could not copy the DEMO database backup to the local protected directory.' }
    }
    finally { & docker exec $ContainerId rm -f $containerFile | Out-Null }
    Write-Host "Backed up DEMO database before applying: $($PendingMigrations -join ', '). Backup: $backupFile"
}

function Restore-DemoDependencies {
    $backendProject = Join-Path $demoWorktree $backendProjectRelativePath
    $frontendDirectory = Join-Path $demoWorktree $frontendRelativePath
    & dotnet restore $backendProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the DEMO backend dependencies.' }
    Push-Location $frontendDirectory
    try {
        & npm ci --legacy-peer-deps
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the DEMO frontend dependencies.' }
    }
    finally { Pop-Location }
}

function Start-ProcessWithEnvironment([hashtable]$Environment, [scriptblock]$Action) {
    $previousValues = @{}
    foreach ($name in $Environment.Keys) {
        $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name], 'Process')
    }
    try { return & $Action }
    finally { foreach ($name in $previousValues.Keys) { [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process') } }
}

function Start-DemoBackend([hashtable]$LocalValues) {
    New-Item -ItemType Directory -Path $demoLogDirectory -Force | Out-Null
    $backendProject = Join-Path $demoWorktree $backendProjectRelativePath
    $stdout = Join-Path $demoLogDirectory 'backend.stdout.log'; $stderr = Join-Path $demoLogDirectory 'backend.stderr.log'
    $environment = @{ ASPNETCORE_ENVIRONMENT = 'Production'; ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$postgresPort;Database=$databaseName;Username=$($LocalValues['POSTGRES_USER']);Password=$($LocalValues['POSTGRES_PASSWORD'])" }
    foreach ($name in $bootstrapSettingNames) { if (-not [string]::IsNullOrWhiteSpace($LocalValues[$name])) { $environment[$name] = $LocalValues[$name] } }
    $process = Start-ProcessWithEnvironment $environment {
        Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-restore', '--project', $backendProject, '--no-launch-profile', '--', '--urls', "https://localhost:$backendHttpsPort") -WorkingDirectory $demoWorktree -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    }
    Write-Host "Started DEMO backend PID $($process.Id). Logs: $stdout and $stderr"
}

function Start-DemoFrontend {
    New-Item -ItemType Directory -Path $demoLogDirectory -Force | Out-Null
    $frontendDirectory = Join-Path $demoWorktree $frontendRelativePath
    $viteCli = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
    if (-not (Test-Path $viteCli)) { throw 'Project-local DEMO Vite is missing after dependency installation.' }
    $stdout = Join-Path $demoLogDirectory 'frontend.stdout.log'; $stderr = Join-Path $demoLogDirectory 'frontend.stderr.log'
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $process = Start-ProcessWithEnvironment @{ VITE_API_PROXY_TARGET = "https://localhost:$backendHttpsPort" } {
        Start-Process -FilePath $node -ArgumentList @('"' + $viteCli + '"', '--host', '127.0.0.1', '--port', $frontendPort) -WorkingDirectory $frontendDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    }
    Write-Host "Started DEMO frontend PID $($process.Id). Logs: $stdout and $stderr"
}

function Ensure-DemoService([ValidateSet('Backend', 'Frontend')] [string]$Kind, [int]$Port, [hashtable]$LocalValues) {
    $owner = Get-PortOwner $Port
    if ($null -ne $owner) {
        if (-not (Test-DemoProjectOwner $owner $Kind)) { throw "DEMO port $Port belongs to '$($owner.Name)' (PID $($owner.ProcessId)), not to the expected DEMO $Kind command. Refusing to reuse or stop it." }
        if ((Test-Healthy $Kind) -and -not $Restart) { Write-Host "Reusing healthy DEMO $Kind on port $Port (PID $($owner.ProcessId))."; return }
        if (-not $Restart) { throw "The DEMO $Kind on port $Port is not healthy. Re-run with -Restart to replace it." }
        Stop-DemoProcess $owner $Port $Kind
    }
    if ($Kind -eq 'Backend') { Start-DemoBackend $LocalValues } else { Start-DemoFrontend }
    for ($attempt = 1; $attempt -le 90; $attempt++) { if (Test-Healthy $Kind) { Write-Host "DEMO $Kind is healthy."; return }; Start-Sleep -Seconds 1 }
    throw "DEMO $Kind did not become healthy. Inspect $demoLogDirectory and do not start a second instance."
}

function Stop-DemoEnvironment {
    foreach ($service in @(@{ Kind = 'Frontend'; Port = $frontendPort }, @{ Kind = 'Backend'; Port = $backendHttpsPort })) {
        $owner = Get-PortOwner $service.Port
        if ($null -eq $owner) { continue }
        if (-not (Test-DemoProjectOwner $owner $service.Kind)) { throw "DEMO port $($service.Port) belongs to '$($owner.Name)' (PID $($owner.ProcessId)), not to the expected DEMO command. Refusing to stop it." }
        Stop-DemoProcess $owner $service.Port $service.Kind
    }
}

if ($Stop) { Stop-DemoEnvironment; return }

$commit = Get-Commit $Ref
$previousCommit = if (Test-Path $deployedShaFile) { (Get-Content -Raw $deployedShaFile).Trim() } else { $null }
$currentDemoWorktreeCommit = Get-DemoWorktreeCommit
$deploymentChanged = $currentDemoWorktreeCommit -ne $commit -or $previousCommit -ne $commit
if (($BackendOnly -or $FrontendOnly) -and $deploymentChanged) { throw 'Partial DEMO service operations are only allowed when both the DEMO worktree and recorded deployment already match the requested commit. Run a full deployment when any revision state differs.' }
$developmentCommit = (@(& git -C $repositoryRoot rev-parse --short HEAD) -join '').Trim()
$developmentChanges = @(& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the active DEV working tree.' }
$developmentState = if ($developmentChanges.Count -eq 0) { 'clean' } else { 'dirty' }
Write-Host "Active DEV worktree: $developmentCommit ($developmentState). Requested DEMO commit: $commit."
if (-not [string]::IsNullOrWhiteSpace($previousCommit)) { Write-Host "Previously recorded DEMO commit: $previousCommit" }
$localValues = Read-LocalEnvironment
Start-SharedPostgres
$postgresContainerId = Get-PostgresContainerId
Confirm-DemoDatabase $postgresContainerId $localValues
if ($deploymentChanged) { Stop-DemoEnvironment }
Ensure-DemoWorktree $commit
$pendingMigrations = Get-PendingMigrations $postgresContainerId $localValues
if ($pendingMigrations.Count -gt 0) { Backup-DemoDatabase $postgresContainerId $localValues $pendingMigrations }
if ($deploymentChanged) { Restore-DemoDependencies }

if (-not $FrontendOnly) { Ensure-DemoService 'Backend' $backendHttpsPort $localValues }
if (-not $BackendOnly) { Ensure-DemoService 'Frontend' $frontendPort $localValues }

if (-not $FrontendOnly -and (Get-HttpStatus "https://localhost:$backendHttpsPort/api/auth/me" -Insecure) -ne '401') { throw 'DEMO authentication endpoint did not return the expected unauthenticated response.' }
if (-not $BackendOnly -and -not $FrontendOnly) {
    Set-Content -Path $deployedShaFile -Value $commit -Encoding UTF8
    Write-Host "DEMO deployed commit: $commit"
}

[CmdletBinding()]
param(
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
$backendProject = Join-Path $repositoryRoot 'backend\src\Tsdt.Api\Tsdt.Api.csproj'
$backendExecutable = Join-Path $repositoryRoot 'backend\src\Tsdt.Api\bin\Debug\net10.0\Tsdt.Api.exe'
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$sharedPostgresComposeFile = Join-Path $repositoryRoot 'infrastructure\docker-compose.yml'
$logDirectory = Join-Path $repositoryRoot '.local\logs\dev'
$frontendPort = 5175
$backendHttpsPort = 7227
$postgresPort = 5432
$databaseName = 'vivatflow_dev'

function Get-PortOwner([int]$Port) {
    try { $connection = Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq $Port | Select-Object -First 1 }
    catch { throw "The listener on port $Port could not be inspected. Refusing to start another process. $($_.Exception.Message)" }
    if ($null -eq $connection) { return $null }
    try { return Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction Stop }
    catch { throw "Port $Port is listening, but its owner could not be inspected. Refusing to start another process. $($_.Exception.Message)" }
}

function Test-DevProjectOwner($Process, [ValidateSet('Backend', 'Frontend')] [string]$Kind) {
    $commandLine = [string]$Process.CommandLine
    if ($Kind -eq 'Backend') {
        return ($Process.Name -eq 'dotnet.exe' -and $commandLine.Contains($backendProject) -and $commandLine.Contains("https://localhost:$backendHttpsPort")) -or
            ($Process.Name -eq 'Tsdt.Api.exe' -and $Process.ExecutablePath -eq $backendExecutable)
    }
    return $Process.Name -eq 'node.exe' -and $commandLine.Contains($frontendDirectory) -and $commandLine -match '(?i)[\\/]vite[\\/]bin[\\/]vite\.js' -and $commandLine -match "--port\s+$frontendPort"
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

function Wait-ForPortRelease([int]$Port) {
    for ($attempt = 1; $attempt -le 30; $attempt++) { if ($null -eq (Get-PortOwner $Port)) { return }; Start-Sleep -Seconds 1 }
    throw "Port $Port was not released within 30 seconds."
}

function Stop-DevProcess($Process, [int]$Port, [string]$Name) {
    Write-Host "Stopping the DEV $Name process (PID $($Process.ProcessId))."
    if (Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue) {
        $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId) -NoNewWindow -Wait -PassThru
        if ($termination.ExitCode -ne 0) {
            $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId, '/F') -NoNewWindow -Wait -PassThru
            if ($termination.ExitCode -ne 0) { throw "Could not stop the DEV $Name process (PID $($Process.ProcessId))." }
        }
    }
    Wait-ForPortRelease $Port
}

function Get-DevDatabaseConfiguration {
    $environmentFile = Join-Path $repositoryRoot '.env'
    if (-not (Test-Path $environmentFile)) { throw '.env is unavailable. Copy .env.example to .env and configure local PostgreSQL credentials.' }
    $values = @{}
    Get-Content $environmentFile | ForEach-Object { if ($_ -match '^\s*([^#=]+)=(.*)$') { $values[$matches[1].Trim()] = $matches[2].Trim() } }
    foreach ($name in @('POSTGRES_USER', 'POSTGRES_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace($values[$name])) { throw ".env is missing $name; cannot configure the DEV database." }
    }
    return [PSCustomObject]@{ ConnectionString = "Host=127.0.0.1;Port=$postgresPort;Database=$databaseName;Username=$($values['POSTGRES_USER']);Password=$($values['POSTGRES_PASSWORD'])"; User = $values['POSTGRES_USER'] }
}

function Ensure-DevDatabase($database) {
    if (-not (Test-Path $sharedPostgresComposeFile)) { throw "Shared PostgreSQL Compose file is missing: $sharedPostgresComposeFile" }
    $containerId = (@(& docker compose --file $sharedPostgresComposeFile ps --quiet postgres) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw 'The shared PostgreSQL service is not running. Start it through the controlled DEMO infrastructure workflow; DEV startup will not start, replace, or stop it.'
    }
    $databaseExists = (@(& docker exec $containerId psql -U $database.User -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$databaseName'") -join '').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect databases in the shared PostgreSQL service.' }
    if ($databaseExists -ne '1') {
        & docker exec $containerId createdb -U $database.User -O $database.User $databaseName
        if ($LASTEXITCODE -ne 0) { throw "Could not create the DEV database '$databaseName' in the shared PostgreSQL service." }
    }
    $databaseExists = (@(& docker exec $containerId psql -U $database.User -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$databaseName'") -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $databaseExists -ne '1') { throw "DEV database '$databaseName' could not be verified." }
}

function Start-ProcessWithEnvironment([hashtable]$Environment, [scriptblock]$Action) {
    $previousValues = @{}
    foreach ($name in $Environment.Keys) {
        $existing = [Environment]::GetEnvironmentVariable($name, 'Process')
        $previousValues[$name] = $existing
        [Environment]::SetEnvironmentVariable($name, [string]$Environment[$name], 'Process')
    }
    try { return & $Action }
    finally {
        foreach ($name in $previousValues.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process')
        }
    }
}

function Start-Backend {
    $database = Get-DevDatabaseConfiguration
    Ensure-DevDatabase $database
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdout = Join-Path $logDirectory 'backend.stdout.log'; $stderr = Join-Path $logDirectory 'backend.stderr.log'
    $environment = @{ ASPNETCORE_ENVIRONMENT = 'Development'; ConnectionStrings__DefaultConnection = $database.ConnectionString; Cors__AllowedOrigins__0 = "http://127.0.0.1:$frontendPort" }
    $process = Start-ProcessWithEnvironment $environment {
        Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-restore', '--project', $backendProject, '--no-launch-profile', '--', '--urls', "https://localhost:$backendHttpsPort") -WorkingDirectory $repositoryRoot -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    }
    Write-Host "Started DEV backend PID $($process.Id). Logs: $stdout and $stderr"
}

function Start-Frontend {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdout = Join-Path $logDirectory 'frontend.stdout.log'; $stderr = Join-Path $logDirectory 'frontend.stderr.log'
    $viteCli = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
    if (-not (Test-Path $viteCli)) { throw 'Project-local Vite is missing. Run npm ci in frontend.' }
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $process = Start-ProcessWithEnvironment @{ VITE_API_PROXY_TARGET = "https://localhost:$backendHttpsPort" } {
        Start-Process -FilePath $node -ArgumentList @('"' + $viteCli + '"', '--host', '127.0.0.1', '--port', $frontendPort) -WorkingDirectory $frontendDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    }
    Write-Host "Started DEV frontend PID $($process.Id). Logs: $stdout and $stderr"
}

function Ensure-Service([ValidateSet('Backend', 'Frontend')] [string]$Kind, [int]$Port) {
    $owner = Get-PortOwner $Port
    if ($null -ne $owner) {
        if (-not (Test-DevProjectOwner $owner $Kind)) { throw "DEV port $Port belongs to '$($owner.Name)' (PID $($owner.ProcessId)), not to the expected DEV $Kind command. Refusing to reuse or stop it." }
        if ((Test-Healthy $Kind) -and -not $Restart) { Write-Host "Reusing healthy DEV $Kind on port $Port (PID $($owner.ProcessId))."; return }
        if (-not $Restart) { throw "The DEV $Kind on port $Port is not healthy. Re-run with -Restart to replace it." }
        Stop-DevProcess $owner $Port $Kind
    }
    if ($Kind -eq 'Backend') { Start-Backend } else { Start-Frontend }
    for ($attempt = 1; $attempt -le 45; $attempt++) { if (Test-Healthy $Kind) { Write-Host "DEV $Kind is healthy."; return }; Start-Sleep -Seconds 1 }
    throw "DEV $Kind did not become healthy. Inspect .local\\logs\\dev and do not start a second instance."
}

function Stop-DevEnvironment {
    foreach ($service in @(@{ Kind = 'Frontend'; Port = $frontendPort }, @{ Kind = 'Backend'; Port = $backendHttpsPort })) {
        $owner = Get-PortOwner $service.Port
        if ($null -eq $owner) { continue }
        if (-not (Test-DevProjectOwner $owner $service.Kind)) { throw "DEV port $($service.Port) belongs to '$($owner.Name)' (PID $($owner.ProcessId)), not to the expected DEV command. Refusing to stop it." }
        Stop-DevProcess $owner $service.Port $service.Kind
    }
}

if ($Stop) { Stop-DevEnvironment; return }
if (-not $FrontendOnly) { Ensure-Service 'Backend' $backendHttpsPort }
if (-not $BackendOnly) { Ensure-Service 'Frontend' $frontendPort }

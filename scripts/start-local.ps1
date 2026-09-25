[CmdletBinding()]
param(
    [switch]$Restart,
    [switch]$BackendOnly,
    [switch]$FrontendOnly
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ($BackendOnly -and $FrontendOnly) {
    throw 'Choose at most one of -BackendOnly and -FrontendOnly.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backendProject = Join-Path $repositoryRoot 'backend\src\Tsdt.Api\Tsdt.Api.csproj'
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$logDirectory = Join-Path $repositoryRoot '.local\logs'
$frontendPort = 5173
$backendHttpsPort = 7226
$postgresPort = 5432

function Get-PortOwner([int]$Port) {
    try {
        $connection = Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq $Port | Select-Object -First 1
    }
    catch {
        throw "The listener on port $Port could not be inspected. Refusing to start another process. $($_.Exception.Message)"
    }
    if ($null -eq $connection) { return $null }

    try {
        return Get-CimInstance Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction Stop
    }
    catch {
        throw "Port $Port is listening, but its owner could not be inspected. Refusing to start another process. $($_.Exception.Message)"
    }
}

function Test-ProjectOwner($Process, [ValidateSet('Backend', 'Frontend')] [string]$Kind) {
    $commandLine = [string]$Process.CommandLine
    if ($Kind -eq 'Backend') {
        $apiOutput = Join-Path $repositoryRoot 'backend\src\Tsdt.Api\bin'
        return ($Process.Name -eq 'Tsdt.Api.exe' -and [string]$Process.ExecutablePath -like "$apiOutput\*") -or
            ($Process.Name -eq 'dotnet.exe' -and $commandLine.Contains($backendProject))
    }

    return $Process.Name -eq 'node.exe' -and $commandLine.Contains($frontendDirectory) -and
        $commandLine -match '(?i)[\\/]vite[\\/]bin[\\/]vite\.js'
}

function Get-HttpStatus([string]$Uri, [switch]$Insecure) {
    $arguments = @('--silent', '--show-error', '--output', 'NUL', '--write-out', '%{http_code}', '--max-time', '5')
    if ($Insecure) { $arguments += '--insecure' }
    $arguments += $Uri
    try {
        $status = (& curl.exe @arguments 2>$null).Trim()
    }
    catch {
        return $null
    }
    if ($LASTEXITCODE -ne 0) { return $null }
    return $status
}

function Test-Healthy([ValidateSet('Backend', 'Frontend')] [string]$Kind) {
    if ($Kind -eq 'Backend') {
        return (Get-HttpStatus "https://localhost:$backendHttpsPort/health" -Insecure) -eq '200'
    }
    return (Get-HttpStatus "http://127.0.0.1:$frontendPort/health") -eq '200'
}

function Wait-ForPortRelease([int]$Port) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if ($null -eq (Get-PortOwner $Port)) { return }
        Start-Sleep -Seconds 1
    }
    throw "Port $Port was not released within 30 seconds."
}

function Stop-ProjectProcess($Process, [int]$Port, [string]$Name) {
    Write-Host "Stopping the existing $Name process (PID $($Process.ProcessId))."
    $runningProcess = Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $runningProcess) {
        $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId) -NoNewWindow -Wait -PassThru
        if ($termination.ExitCode -ne 0) {
            Write-Warning "The $Name process did not accept normal termination; forcing it after the controlled stop attempt."
            $termination = Start-Process -FilePath 'taskkill.exe' -ArgumentList @('/PID', $Process.ProcessId, '/F') -NoNewWindow -Wait -PassThru
            if ($termination.ExitCode -ne 0) {
                throw "Could not stop the existing $Name process (PID $($Process.ProcessId))."
            }
        }
    }
    Wait-ForPortRelease $Port
}

function Set-LocalDatabaseConnectionIfNeeded {
    if (-not [string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) { return }

    $environmentFile = Join-Path $repositoryRoot '.env'
    if (-not (Test-Path $environmentFile)) {
        throw 'ConnectionStrings__DefaultConnection is not set and .env is unavailable. Configure the local database before starting the backend.'
    }

    $values = @{}
    Get-Content $environmentFile | ForEach-Object {
        if ($_ -match '^\s*([^#=]+)=(.*)$') { $values[$matches[1].Trim()] = $matches[2].Trim() }
    }
    foreach ($name in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace($values[$name])) {
            throw ".env is missing $name; cannot configure the backend connection."
        }
    }
    if ($values.ContainsKey('POSTGRES_PORT') -and $values['POSTGRES_PORT'] -ne "$postgresPort") {
        throw ".env sets POSTGRES_PORT to '$($values['POSTGRES_PORT'])', but the canonical local PostgreSQL port is $postgresPort."
    }
    $env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=$postgresPort;Database=$($values['POSTGRES_DB']);Username=$($values['POSTGRES_USER']);Password=$($values['POSTGRES_PASSWORD'])"
}

function Start-Backend {
    Set-LocalDatabaseConnectionIfNeeded
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdout = Join-Path $logDirectory 'backend.stdout.log'
    $stderr = Join-Path $logDirectory 'backend.stderr.log'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-restore', '--project', $backendProject, '--launch-profile', 'https') -WorkingDirectory $repositoryRoot -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    Write-Host "Started backend PID $($process.Id). Logs: $stdout and $stderr"
}

function Start-Frontend {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stdout = Join-Path $logDirectory 'frontend.stdout.log'
    $stderr = Join-Path $logDirectory 'frontend.stderr.log'
    $viteCli = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
    if (-not (Test-Path $viteCli)) { throw 'Project-local Vite is missing. Run npm ci in frontend.' }
    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $process = Start-Process -FilePath $node -ArgumentList @('"' + $viteCli + '"') -WorkingDirectory $frontendDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    Write-Host "Started frontend PID $($process.Id). Logs: $stdout and $stderr"
}

function Ensure-Service([ValidateSet('Backend', 'Frontend')] [string]$Kind, [int]$Port) {
    $owner = Get-PortOwner $Port
    if ($null -ne $owner) {
        if (-not (Test-ProjectOwner $owner $Kind)) {
            throw "Port $Port belongs to '$($owner.Name)' (PID $($owner.ProcessId)), not the TSDT $Kind. Refusing to start another process."
        }

        if ((Test-Healthy $Kind) -and -not $Restart) {
            Write-Host "Reusing healthy TSDT $Kind on port $Port (PID $($owner.ProcessId))."
            return
        }

        if (-not $Restart) {
            throw "The TSDT $Kind on port $Port is not healthy. Re-run with -Restart to replace it."
        }

        Stop-ProjectProcess $owner $Port $Kind
    }

    if ($Kind -eq 'Backend') { Start-Backend } else { Start-Frontend }
    for ($attempt = 1; $attempt -le 45; $attempt++) {
        if (Test-Healthy $Kind) {
            Write-Host "TSDT $Kind is healthy."
            return
        }
        Start-Sleep -Seconds 1
    }
    throw "TSDT $Kind did not become healthy. Inspect .local\\logs and do not start a second instance."
}

if (-not $FrontendOnly) { Ensure-Service 'Backend' $backendHttpsPort }
if (-not $BackendOnly) { Ensure-Service 'Frontend' $frontendPort }

[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$QuickTunnel,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
if ($Stop -and ($ConfigPath -or $QuickTunnel)) { throw '-Stop cannot be combined with -ConfigPath or -QuickTunnel.' }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$demoRoot = Join-Path $repositoryRoot '.local\demo'
$logDirectory = Join-Path $repositoryRoot '.local\logs\demo'
$pidFile = Join-Path $demoRoot 'tunnel.pid'
$urlFile = Join-Path $demoRoot 'tunnel-url.txt'
$frontendPort = 5173
$cloudflared = (Get-Command cloudflared.exe -ErrorAction Stop).Source

function Get-HttpStatus([string]$Uri) {
    try { $status = (& curl.exe --silent --show-error --output NUL --write-out '%{http_code}' --max-time 5 $Uri 2>$null).Trim() } catch { return $null }
    if ($LASTEXITCODE -ne 0) { return $null }
    return $status
}

function Stop-DemoTunnel {
    if (-not (Test-Path $pidFile)) { return }
    $processId = (Get-Content -Raw $pidFile).Trim()
    if ($processId -notmatch '^\d+$') { throw "DEMO tunnel PID file is invalid: $pidFile" }
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        if ($process.ProcessName -ne 'cloudflared') { throw "DEMO tunnel PID $processId is not cloudflared. Refusing to stop it." }
        Stop-Process -Id $processId -ErrorAction Stop
    }
    Remove-Item -LiteralPath $pidFile -Force
    Remove-Item -LiteralPath $urlFile -Force -ErrorAction SilentlyContinue
}

if ($Stop) { Stop-DemoTunnel; return }
if ((Get-HttpStatus "http://127.0.0.1:$frontendPort/health") -ne '200') { throw "DEMO frontend is not healthy on port $frontendPort. Start DEMO before exposing it." }

if (Test-Path $pidFile) { Stop-DemoTunnel }
New-Item -ItemType Directory -Path $demoRoot -Force | Out-Null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$stdout = Join-Path $logDirectory 'cloudflared.stdout.log'
$stderr = Join-Path $logDirectory 'cloudflared.stderr.log'

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $defaultConfig = Join-Path $env:USERPROFILE '.cloudflared\config.yml'
    if (Test-Path $defaultConfig) { $ConfigPath = $defaultConfig }
}

if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $resolvedConfig = (Resolve-Path $ConfigPath).Path
    $config = Get-Content -Raw $resolvedConfig
    if ($config -notmatch '(?im)service:\s*http://(127\.0\.0\.1|localhost):5173(?:\s|$)') { throw "Cloudflare configuration must explicitly route to http://127.0.0.1:5173: $resolvedConfig" }
    $arguments = @('--no-autoupdate', '--config', $resolvedConfig, 'tunnel', 'run')
    $description = "named configuration $resolvedConfig"
}
elseif ($QuickTunnel) {
    $arguments = @('--no-autoupdate', 'tunnel', '--url', "http://127.0.0.1:$frontendPort")
    $description = 'temporary Quick Tunnel'
}
else { throw 'No named Cloudflare configuration was found. Provide -ConfigPath or explicitly opt in to the temporary fallback with -QuickTunnel.' }

$process = Start-Process -FilePath $cloudflared -ArgumentList $arguments -WorkingDirectory $repositoryRoot -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
Set-Content -Path $pidFile -Value $process.Id -Encoding ASCII
if ($QuickTunnel) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $urlMatch = Select-String -Path $stderr -Pattern 'https://[a-z0-9-]+\.trycloudflare\.com' -AllMatches -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($urlMatch) {
            $url = $urlMatch.Matches[0].Value
            Set-Content -Path $urlFile -Value $url -Encoding ASCII
            Write-Host "Started DEMO ${description}: $url"
            return
        }
        Start-Sleep -Seconds 1
    }
    throw "The DEMO Quick Tunnel did not emit a URL. Inspect $stderr."
}
Write-Host "Started DEMO tunnel using $description."

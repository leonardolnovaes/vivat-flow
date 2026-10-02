[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Variable PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'backend\Tsdt.sln'
Write-Host 'Restoring backend packages once. NuGet may use locally cached packages; unavailable sources are ignored only when every required package is already available.'
& dotnet restore $solution --ignore-failed-sources --disable-parallel
if ($LASTEXITCODE -ne 0) {
    throw 'Restore failed. Check package source availability and package cache. No signature or vulnerability checks were disabled.'
}
Write-Host 'Backend restore complete.'

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (Get-Variable PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'backend\Tsdt.sln'
$frontend = Join-Path $root 'frontend'
$steps = [ordered]@{
    'Backend Release build' = 'pending'
    'Backend unit tests' = 'pending'
    'Frontend lint' = 'pending'
    'Frontend production build' = 'pending'
}
$failed = $false

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "`n==> $Name"
    try {
        & $Action
        if ($LASTEXITCODE -ne 0) { throw "Command exited with code $LASTEXITCODE." }
        $steps[$Name] = 'PASS'
    }
    catch {
        $steps[$Name] = 'FAIL'
        $hint = switch ($Name) {
            'Backend Release build' { ' If the dependency graph changed, run .\scripts\restore.ps1 once.' }
            'Backend unit tests' { ' Inspect the unit-test failure above; do not skip unit tests.' }
            'Frontend lint' { ' Fix the reported lint diagnostics.' }
            'Frontend production build' { ' Fix the reported TypeScript or Vite diagnostics.' }
        }
        throw "$Name failed: $($_.Exception.Message)$hint"
    }
}

try {
    foreach ($project in @('src\Tsdt.Api', 'tests\Tsdt.Tests', 'tools\Tsdt.LocalAdminReset')) {
        $assets = Join-Path $root "backend\$project\obj\project.assets.json"
        if (-not (Test-Path $assets)) {
            throw "Missing restored assets: $assets. Run .\scripts\restore.ps1 once, then retry validation."
        }
    }
    if (-not (Test-Path (Join-Path $frontend 'node_modules\.bin\oxlint.cmd')) -or
        -not (Test-Path (Join-Path $frontend 'node_modules\vite\bin\vite.js'))) {
        throw 'Frontend dependencies are missing. Run npm ci in frontend, then retry validation.'
    }

    # Each project gets its own Validation directory. Live Debug and Release APIs
    # continue using their regular outputs, so validation cannot overwrite a DLL in use.
    $validationOutput = '-p:BaseOutputPath=bin/Validation/'
    Invoke-Step 'Backend Release build' {
        & dotnet build $solution --configuration Release --no-restore $validationOutput
    }
    Invoke-Step 'Backend unit tests' {
        & dotnet test $solution --configuration Release --no-build --no-restore $validationOutput --filter 'Category=Unit'
    }
    Push-Location $frontend
    try {
        Invoke-Step 'Frontend lint' { & npm.cmd run lint }
        Invoke-Step 'Frontend production build' { & npm.cmd run build }
    }
    finally { Pop-Location }
}
catch {
    $failed = $true
    Write-Error -ErrorAction Continue $_
}
finally {
    Write-Host "`nValidation summary:"
    foreach ($step in $steps.Keys) { Write-Host "  $step`: $($steps[$step])" }
    if ($failed) { Write-Host 'Fix the first failure above and rerun .\scripts\validate.ps1.' }
}
if ($failed) { exit 1 }

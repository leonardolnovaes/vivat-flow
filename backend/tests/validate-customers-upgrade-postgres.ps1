[CmdletBinding()]
param(
    [ValidateRange(1025, 65535)]
    [int]$PostgresPort = 55434
)

$ErrorActionPreference = 'Stop'

function New-ValidationSecret {
    $bytes = [byte[]]::new(24)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return "M2!aA1$([Convert]::ToBase64String($bytes).Replace('+', 'A').Replace('/', 'b').Replace('=', ''))"
}

function Wait-ForPostgres([string]$ContainerName, [string]$DatabaseName, [string]$UserName) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        docker exec $ContainerName pg_isready -U $UserName -d $DatabaseName *> $null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw 'The isolated PostgreSQL container did not become ready.'
}

function Invoke-Postgres([string]$ContainerName, [string]$UserName, [string]$DatabaseName, [string]$Query) {
    $Query | docker exec -i $ContainerName psql -v ON_ERROR_STOP=1 -U $UserName -d $DatabaseName *> $null
    if ($LASTEXITCODE -ne 0) { throw 'The PostgreSQL validation query failed.' }
}

function Get-PostgresScalar([string]$ContainerName, [string]$UserName, [string]$DatabaseName, [string]$Query) {
    $result = $Query | docker exec -i $ContainerName psql -U $UserName -d $DatabaseName -tA
    if ($LASTEXITCODE -ne 0) { throw 'The PostgreSQL validation query failed.' }
    return ($result | Out-String).Trim()
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$containerName = "tsdt-customers-upgrade-$PID"
$databaseName = 'tsdt_customers_upgrade'
$databaseUser = 'tsdt_validation'
$databasePassword = New-ValidationSecret
$connectionString = "Host=127.0.0.1;Port=$PostgresPort;Database=$databaseName;Username=$databaseUser;Password=$databasePassword"

try {
    if (Get-NetTCPConnection -State Listen -LocalPort $PostgresPort -ErrorAction SilentlyContinue) {
        throw "Port $PostgresPort is already in use; choose another -PostgresPort value."
    }
    docker run --detach --rm --name $containerName --publish "127.0.0.1:$PostgresPort`:5432" --env "POSTGRES_DB=$databaseName" --env "POSTGRES_USER=$databaseUser" --env "POSTGRES_PASSWORD=$databasePassword" postgres:18-alpine *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated PostgreSQL container.' }
    Wait-ForPostgres $containerName $databaseName $databaseUser

    $env:ConnectionStrings__DefaultConnection = $connectionString
    dotnet tool run dotnet-ef database update 20260923010000_HardenUserProfileValidation --configuration Release --no-build --project "$repositoryRoot\backend\src\Tsdt.Api\Tsdt.Api.csproj" --startup-project "$repositoryRoot\backend\src\Tsdt.Api\Tsdt.Api.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Applying the Module 1 schema failed.' }

    Invoke-Postgres $containerName $databaseUser $databaseName 'INSERT INTO "AspNetUsers" ("Id", "IsActive", "MustChangePassword", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "FullName") VALUES (''legacy-module1-user'', true, false, ''legacy@example.test'', ''LEGACY@EXAMPLE.TEST'', ''legacy@example.test'', ''LEGACY@EXAMPLE.TEST'', true, NULL, NULL, NULL, false, false, false, 0, ''Legacy Module 1 User'');'

    dotnet tool run dotnet-ef database update --configuration Release --no-build --project "$repositoryRoot\backend\src\Tsdt.Api\Tsdt.Api.csproj" --startup-project "$repositoryRoot\backend\src\Tsdt.Api\Tsdt.Api.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'Upgrading the Module 1 schema to Customers Phase 1 failed.' }

    $legacyUserCount = Get-PostgresScalar $containerName $databaseUser $databaseName 'SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id" = ''legacy-module1-user'' AND "FullName" = ''Legacy Module 1 User'';'
    $migrationCount = Get-PostgresScalar $containerName $databaseUser $databaseName 'SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" IN (''20260922200327_CreateIdentityFoundation'', ''20260922213000_AddUserAdministration'', ''20260923010000_HardenUserProfileValidation'', ''20260924145831_AddCustomersPhase1'');'
    $customerTables = Get-PostgresScalar $containerName $databaseUser $databaseName 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = ''public'' AND table_name IN (''Customers'', ''CustomerContacts'', ''CustomerUnits'', ''CustomerAuditRecords'');'
    if ($legacyUserCount -ne '1') { throw 'The existing Module 1 user was not preserved during upgrade.' }
    if ($migrationCount -ne '4') { throw 'The expected migration history was not preserved during upgrade.' }
    if ($customerTables -ne '4') { throw 'The Customers Phase 1 tables were not created during upgrade.' }
    Write-Output 'Customers PostgreSQL upgrade from a Module 1 schema validation passed.'
}
finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    docker rm --force $containerName *> $null
}

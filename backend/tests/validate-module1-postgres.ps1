[CmdletBinding()]
param(
    [ValidateRange(1025, 65535)]
    [int]$PostgresPort = 55432,
    [ValidateRange(1025, 65535)]
    [int]$ApiPort = 57226
)

$ErrorActionPreference = 'Stop'

function New-ValidationSecret {
    $bytes = [byte[]]::new(24)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }
    return "M1!aA1$([Convert]::ToBase64String($bytes).Replace('+', 'A').Replace('/', 'b').Replace('=', ''))"
}

function Assert-Equal([object]$Actual, [object]$Expected, [string]$Description) {
    if ($Actual -ne $Expected) {
        throw "$Description. Expected '$Expected', received '$Actual'."
    }
}

function Wait-ForPostgres([string]$ContainerName, [string]$DatabaseName, [string]$UserName) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        docker exec $ContainerName pg_isready -U $UserName -d $DatabaseName *> $null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw 'The isolated PostgreSQL container did not become ready.'
}

function Start-ValidationApi([hashtable]$Environment, [string]$ApiDllPath, [int]$Port) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.Arguments = "`"$ApiDllPath`" --urls http://127.0.0.1:$Port"
    $startInfo.WorkingDirectory = $PSScriptRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($name in $Environment.Keys) {
        $startInfo.EnvironmentVariables[$name] = $Environment[$name]
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if ($process.HasExited) { throw 'The validation API exited before becoming healthy.' }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2
            if ($health.status -eq 'Healthy') { return $process }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    throw 'The validation API did not become healthy.'
}

function Get-PostgresScalar([string]$ContainerName, [string]$UserName, [string]$DatabaseName, [string]$Query) {
    $result = $Query | docker exec -i $ContainerName psql -U $UserName -d $DatabaseName -tA
    if ($LASTEXITCODE -ne 0) { throw 'A validation query against the isolated PostgreSQL database failed.' }
    return ($result | Out-String).Trim()
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiDll = Join-Path $repositoryRoot 'backend\src\Tsdt.Api\bin\Validation\Release\net10.0\Tsdt.Api.dll'
if (-not (Test-Path $apiDll)) {
    throw 'Build the Release API first: .\scripts\validate.ps1'
}

$containerName = "tsdt-module1-postgres-$PID"
$databaseName = 'tsdt_module1_validation'
$databaseUser = 'tsdt_validation'
$databasePassword = New-ValidationSecret
$bootstrapPassword = New-ValidationSecret
$bootstrapEmail = "module1-bootstrap-$PID@example.test"
$apiProcess = $null

try {
    if (Get-NetTCPConnection -State Listen -LocalPort $PostgresPort -ErrorAction SilentlyContinue) {
        throw "Port $PostgresPort is already in use; choose another -PostgresPort value."
    }
    if (Get-NetTCPConnection -State Listen -LocalPort $ApiPort -ErrorAction SilentlyContinue) {
        throw "Port $ApiPort is already in use; choose another -ApiPort value."
    }

    docker run --detach --rm --name $containerName --publish "127.0.0.1:$PostgresPort`:5432" --env "POSTGRES_DB=$databaseName" --env "POSTGRES_USER=$databaseUser" --env "POSTGRES_PASSWORD=$databasePassword" postgres:18-alpine *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated PostgreSQL container.' }
    Wait-ForPostgres $containerName $databaseName $databaseUser

    $validationEnvironment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=$PostgresPort;Database=$databaseName;Username=$databaseUser;Password=$databasePassword"
        BootstrapAdmin__Email = $bootstrapEmail
        BootstrapAdmin__FullName = 'Module 1 Validation Administrator'
        BootstrapAdmin__Password = $bootstrapPassword
    }

    $apiProcess = Start-ValidationApi $validationEnvironment $apiDll $ApiPort
    Stop-Process -Id $apiProcess.Id -ErrorAction Stop
    $apiProcess.WaitForExit()
    $apiProcess = Start-ValidationApi $validationEnvironment $apiDll $ApiPort

    $integrityQuery = @'
DO $$
BEGIN
    INSERT INTO "Customers" ("Id", "LegalName", "Cnpj", "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "CreatedByUserId", "UpdatedByUserId", "Version") VALUES
        ('11111111-1111-1111-1111-111111111111', 'Constraint Validation Customer', '11222333000181', true, now(), now(), 'validation', 'validation', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
    BEGIN
        INSERT INTO "Customers" ("Id", "LegalName", "Cnpj", "IsActive", "CreatedAtUtc", "UpdatedAtUtc", "CreatedByUserId", "UpdatedByUserId", "Version") VALUES
            ('11111111-1111-1111-1111-111111111112', 'Duplicate CNPJ', '11222333000181', true, now(), now(), 'validation', 'validation', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab');
        RAISE EXCEPTION 'CNPJ unique constraint was not enforced';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    INSERT INTO "CustomerContacts" ("Id", "CustomerId", "Name", "IsPrimary", "IsActive", "CreatedAtUtc", "UpdatedAtUtc") VALUES
        ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Primary Contact', true, true, now(), now());
    BEGIN
        INSERT INTO "CustomerContacts" ("Id", "CustomerId", "Name", "IsPrimary", "IsActive", "CreatedAtUtc", "UpdatedAtUtc") VALUES
            ('22222222-2222-2222-2222-222222222223', '11111111-1111-1111-1111-111111111111', 'Second Primary Contact', true, true, now(), now());
        RAISE EXCEPTION 'Contact primary partial unique index was not enforced';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
    INSERT INTO "CustomerUnits" ("Id", "CustomerId", "Name", "Street", "Number", "City", "StateCode", "IsPrimary", "IsActive", "CreatedAtUtc", "UpdatedAtUtc") VALUES
        ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 'Primary Unit', 'Street', '1', 'City', 'SP', true, true, now(), now());
    BEGIN
        INSERT INTO "CustomerUnits" ("Id", "CustomerId", "Name", "Street", "Number", "City", "StateCode", "IsPrimary", "IsActive", "CreatedAtUtc", "UpdatedAtUtc") VALUES
            ('33333333-3333-3333-3333-333333333334', '11111111-1111-1111-1111-111111111111', 'Second Primary Unit', 'Street', '2', 'City', 'SP', true, true, now(), now());
        RAISE EXCEPTION 'Unit primary partial unique index was not enforced';
    EXCEPTION WHEN unique_violation THEN NULL;
    END;
END $$;
'@
    $integrityQuery | docker exec -i $containerName psql -v ON_ERROR_STOP=1 -U $databaseUser -d $databaseName *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Customers database constraints were not enforced.' }

    $migrationQuery = 'SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" IN (''20260922200327_CreateIdentityFoundation'', ''20260922213000_AddUserAdministration'', ''20260923010000_HardenUserProfileValidation'', ''20260924145831_AddCustomersPhase1'');'
    $roleQuery = 'SELECT COUNT(*) FROM "AspNetRoles" WHERE "Name" IN (''ADMIN'', ''MANAGER'', ''USER'');'
    $adminQuery = 'SELECT COUNT(*) FROM "AspNetUsers" u JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id" JOIN "AspNetRoles" r ON r."Id" = ur."RoleId" WHERE r."Name" = ''ADMIN'' AND u."IsActive" AND u."MustChangePassword";'
    $profileSchemaQuery = 'SELECT COUNT(*) FROM information_schema.columns WHERE table_name = ''AspNetUsers'' AND ((column_name = ''FullName'' AND character_maximum_length = 120 AND is_nullable = ''NO'') OR (column_name IN (''Email'', ''NormalizedEmail'') AND character_maximum_length = 254));'
    $customerTablesQuery = 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = ''public'' AND table_name IN (''Customers'', ''CustomerContacts'', ''CustomerUnits'', ''CustomerAuditRecords'');'
    $customerIndexesQuery = 'SELECT COUNT(*) FROM pg_indexes WHERE schemaname = ''public'' AND indexname IN (''IX_Customers_Cnpj'', ''IX_Customers_IsActive_LegalName'', ''IX_CustomerContacts_CustomerId_IsPrimary'', ''IX_CustomerUnits_CustomerId_IsPrimary'', ''IX_CustomerAuditRecords_CustomerId_OccurredAtUtc'');'
    $partialIndexesQuery = 'SELECT COUNT(*) FROM pg_indexes WHERE schemaname = ''public'' AND indexname IN (''IX_CustomerContacts_CustomerId_IsPrimary'', ''IX_CustomerUnits_CustomerId_IsPrimary'') AND indexdef LIKE ''%WHERE ("IsActive" AND "IsPrimary")%'';'
    $migrationCount = Get-PostgresScalar $containerName $databaseUser $databaseName $migrationQuery
    $roleCount = Get-PostgresScalar $containerName $databaseUser $databaseName $roleQuery
    $adminCount = Get-PostgresScalar $containerName $databaseUser $databaseName $adminQuery
    $profileSchemaCount = Get-PostgresScalar $containerName $databaseUser $databaseName $profileSchemaQuery
    $customerTablesCount = Get-PostgresScalar $containerName $databaseUser $databaseName $customerTablesQuery
    $customerIndexesCount = Get-PostgresScalar $containerName $databaseUser $databaseName $customerIndexesQuery
    $partialIndexesCount = Get-PostgresScalar $containerName $databaseUser $databaseName $partialIndexesQuery
    Assert-Equal $migrationCount '4' 'All currently applied migrations, including Customers Phase 1, must be applied'
    Assert-Equal $roleCount '3' 'Exactly the three Module 1 application roles must exist'
    Assert-Equal $adminCount '1' 'Bootstrap must remain idempotent and create one active ADMIN requiring password change'
    Assert-Equal $profileSchemaCount '3' 'The hardened user-profile columns must have their final schema'
    Assert-Equal $customerTablesCount '4' 'Customers tables must be created'
    Assert-Equal $customerIndexesCount '5' 'Customers indexes must be created'
    Assert-Equal $partialIndexesCount '2' 'Customers active-primary partial indexes must be created'
    Write-Output 'Module 1 regression and Customers clean PostgreSQL migration validation passed.'
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -ErrorAction SilentlyContinue
    }
    docker rm --force $containerName *> $null
}

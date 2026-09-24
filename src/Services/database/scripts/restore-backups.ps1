param(
    [string]$EnvFile = ".env",
    [string]$BackupDir = "database/database-backups",
    [switch]$Replace
)

$ErrorActionPreference = "Stop"

$databases = @(
    "StealDealIdentityDb",
    "StealDealNotificationDb",
    "StealDealStoreDb",
    "StealDealOrderDb",
    "StealDealPaymentDb"
)

function Get-EnvValue {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not (Test-Path $Path)) {
        throw "Environment file '$Path' was not found."
    }

    $line = Get-Content $Path |
        Where-Object { $_ -match "^\s*$Name\s*=" } |
        Select-Object -First 1

    if (-not $line) {
        throw "Environment variable '$Name' was not found in '$Path'."
    }

    return ($line -replace "^\s*$Name\s*=", "").Trim().Trim('"').Trim("'")
}

function Invoke-Sql {
    param(
        [string]$Query
    )

    docker compose --env-file $EnvFile exec -T sqlserver $script:SqlCmdPath `
        -S localhost `
        -U sa `
        -P $script:SqlPassword `
        -C `
        -Q $Query
}

$script:SqlPassword = Get-EnvValue -Path $EnvFile -Name "MSSQL_SA_PASSWORD"

docker compose --env-file $EnvFile up -d sqlserver

$script:SqlCmdPath = (
    docker compose --env-file $EnvFile exec -T sqlserver sh -c "if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then echo /opt/mssql-tools18/bin/sqlcmd; else echo /opt/mssql-tools/bin/sqlcmd; fi"
).Trim()

Write-Host "Waiting for SQL Server..."
for ($attempt = 1; $attempt -le 30; $attempt++) {
    try {
        Invoke-Sql -Query "SELECT 1" | Out-Null
        break
    }
    catch {
        if ($attempt -eq 30) {
            throw
        }

        Start-Sleep -Seconds 5
    }
}

docker compose --env-file $EnvFile exec -T sqlserver mkdir -p /var/opt/mssql/backup

foreach ($database in $databases) {
    $backupPath = Join-Path $BackupDir "$database.bak"

    if (-not (Test-Path $backupPath)) {
        Write-Host "Skipping $database. Backup file not found: $backupPath"
        continue
    }

    $existingDatabase = Invoke-Sql -Query "SET NOCOUNT ON; SELECT DB_ID(N'$database');" |
        Select-String -Pattern "\d+" |
        Select-Object -First 1

    if ($existingDatabase -and -not $Replace) {
        Write-Host "Skipping $database. Database already exists. Use -Replace to overwrite it."
        continue
    }

    $containerBackupPath = "/var/opt/mssql/backup/$database.bak"
    Write-Host "Copying $backupPath to sqlserver:$containerBackupPath"
    docker compose --env-file $EnvFile cp $backupPath "sqlserver:$containerBackupPath"

    $fileListQuery = "RESTORE FILELISTONLY FROM DISK = N'$containerBackupPath';"
    $fileList = docker compose --env-file $EnvFile exec -T sqlserver $script:SqlCmdPath `
        -S localhost `
        -U sa `
        -P $script:SqlPassword `
        -C `
        -W `
        -s "," `
        -Q $fileListQuery

    $logicalNames = $fileList |
        Select-String -Pattern "^[^,\s][^,]*," |
        ForEach-Object { ($_.Line -split ",")[0].Trim() } |
        Where-Object { $_ -and $_ -ne "LogicalName" } |
        Select-Object -First 2

    if ($logicalNames.Count -lt 2) {
        throw "Could not read logical file names from '$backupPath'."
    }

    $dataLogicalName = $logicalNames[0]
    $logLogicalName = $logicalNames[1]

    if ($Replace) {
        Invoke-Sql -Query "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; END"
    }

    $replaceOption = if ($Replace) { ", REPLACE" } else { "" }
    $restoreQuery = @"
RESTORE DATABASE [$database]
FROM DISK = N'$containerBackupPath'
WITH
    MOVE N'$dataLogicalName' TO N'/var/opt/mssql/data/$database.mdf',
    MOVE N'$logLogicalName' TO N'/var/opt/mssql/data/${database}_log.ldf',
    RECOVERY$replaceOption;
"@

    Write-Host "Restoring $database..."
    Invoke-Sql -Query $restoreQuery
    Write-Host "$database restored."
}

Write-Host "Backup restore completed."

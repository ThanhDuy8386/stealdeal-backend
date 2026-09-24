param(
    [string]$EnvFile = ".env",
    [string]$BackupDir = "database/database-backups",
    [switch]$Replace
)

$ErrorActionPreference = "Stop"

docker compose --env-file $EnvFile up -d sqlserver

if ($Replace) {
    & "$PSScriptRoot\restore-backups.ps1" -EnvFile $EnvFile -BackupDir $BackupDir -Replace
}
else {
    & "$PSScriptRoot\restore-backups.ps1" -EnvFile $EnvFile -BackupDir $BackupDir
}

& "$PSScriptRoot\migrate-databases.ps1" -EnvFile $EnvFile

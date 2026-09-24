param(
    [string]$EnvFile = ".env"
)

$ErrorActionPreference = "Stop"

docker compose --env-file $EnvFile up -d sqlserver
docker compose --env-file $EnvFile --profile database build database-migrator
docker compose --env-file $EnvFile --profile database run --rm database-migrator

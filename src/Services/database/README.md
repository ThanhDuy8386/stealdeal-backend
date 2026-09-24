# Docker Database Setup

## First run

Create `.env` from `.env.example`, then set a strong SQL Server password:

```env
MSSQL_SA_PASSWORD=StealDeal@12345
```

On a new Docker volume, create schema first:

```powershell
.\database\scripts\migrate-databases.ps1
```

Then start everything:

```powershell
docker compose --env-file .env up -d --build
```

The migration script applies EF migrations for:

- `StealDealIdentityDb`
- `StealDealNotificationDb`
- `StealDealStoreDb`
- `StealDealOrderDb`
- `StealDealPaymentDb`

This creates schema/tables, but does not import development data. Normal
`docker compose up` does not run migrations automatically.

## Restore development data

Export `.bak` files from local SQL Server and put them in:

```text
database/database-backups/
```

Expected names:

```text
StealDealIdentityDb.bak
StealDealNotificationDb.bak
StealDealStoreDb.bak
StealDealOrderDb.bak
StealDealPaymentDb.bak
```

Restore missing databases and then apply migrations:

```powershell
.\database\scripts\setup-dev-database.ps1
```

Overwrite existing Docker databases:

```powershell
.\database\scripts\setup-dev-database.ps1 -Replace
```

## Future migrations

When new EF migration files are added, run this once:

```powershell
.\database\scripts\migrate-databases.ps1
```

EF uses `__EFMigrationsHistory`, so only missing migrations are applied.

## Notes

- Docker SQL Server data is stored in the `sqlserver-data` volume.
- `docker compose down` keeps database data.
- `docker compose down -v` deletes the SQL Server volume and all database data.
- Connect from SSMS/Azure Data Studio with `localhost,1433`, login `sa`, and `MSSQL_SA_PASSWORD`.
- `.bak` files are ignored by git; each developer can keep their own local data set.

@echo off
cd /d "%~dp0"
start "Identity API" cmd /k dotnet run --project "src\Services\Identity\StealDeal.Services.Identity.API\StealDeal.Services.Identity.API.csproj" --launch-profile http
start "Store API" cmd /k dotnet run --project "src\Services\Store\StealDeal.Services.Store.API\StealDeal.Services.Store.API.csproj" --launch-profile http

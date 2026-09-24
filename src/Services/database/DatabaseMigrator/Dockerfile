FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["database/DatabaseMigrator/StealDeal.DatabaseMigrator.csproj", "database/DatabaseMigrator/"]

COPY ["Identity/StealDeal.Services.Identity.API/StealDeal.Services.Identity.API.csproj", "Identity/StealDeal.Services.Identity.API/"]
COPY ["Identity/StealDeal.Services.Identity.Application/StealDeal.Services.Identity.Application.csproj", "Identity/StealDeal.Services.Identity.Application/"]
COPY ["Identity/StealDeal.Services.Identity.Domain/StealDeal.Services.Identity.Domain.csproj", "Identity/StealDeal.Services.Identity.Domain/"]
COPY ["Identity/StealDeal.Services.Identity.Infrastructure/StealDeal.Services.Identity.Infrastructure.csproj", "Identity/StealDeal.Services.Identity.Infrastructure/"]

COPY ["Notification/StealDeal.Services.Notification.API/StealDeal.Services.Notification.API.csproj", "Notification/StealDeal.Services.Notification.API/"]
COPY ["Notification/StealDeal.Services.Notification.Application/StealDeal.Services.Notification.Application.csproj", "Notification/StealDeal.Services.Notification.Application/"]
COPY ["Notification/StealDeal.Services.Notification.Domain/StealDeal.Services.Notification.Domain.csproj", "Notification/StealDeal.Services.Notification.Domain/"]
COPY ["Notification/StealDeal.Services.Notification.Infrastructure/StealDeal.Services.Notification.Infrastructure.csproj", "Notification/StealDeal.Services.Notification.Infrastructure/"]

COPY ["Order/StealDeal.Services.Order.API/StealDeal.Services.Order.API.csproj", "Order/StealDeal.Services.Order.API/"]
COPY ["Order/StealDeal.Services.Order.Application/StealDeal.Services.Order.Application.csproj", "Order/StealDeal.Services.Order.Application/"]
COPY ["Order/StealDeal.Services.Order.Domain/StealDeal.Services.Order.Domain.csproj", "Order/StealDeal.Services.Order.Domain/"]
COPY ["Order/StealDeal.Services.Order.Infrastructure/StealDeal.Services.Order.Infrastructure.csproj", "Order/StealDeal.Services.Order.Infrastructure/"]

COPY ["Payment/StealDeal.Services.Payment.API/StealDeal.Services.Payment.API.csproj", "Payment/StealDeal.Services.Payment.API/"]
COPY ["Payment/StealDeal.Services.Payment.Application/StealDeal.Services.Payment.Application.csproj", "Payment/StealDeal.Services.Payment.Application/"]
COPY ["Payment/StealDeal.Services.Payment.Domain/StealDeal.Services.Payment.Domain.csproj", "Payment/StealDeal.Services.Payment.Domain/"]
COPY ["Payment/StealDeal.Services.Payment.Infrastructure/StealDeal.Services.Payment.Infrastructure.csproj", "Payment/StealDeal.Services.Payment.Infrastructure/"]

COPY ["Store/StealDeal.Services.Store.API/StealDeal.Services.Store.API.csproj", "Store/StealDeal.Services.Store.API/"]
COPY ["Store/StealDeal.Services.Store.Application/StealDeal.Services.Store.Application.csproj", "Store/StealDeal.Services.Store.Application/"]
COPY ["Store/StealDeal.Services.Store.Domain/StealDeal.Services.Store.Domain.csproj", "Store/StealDeal.Services.Store.Domain/"]
COPY ["Store/StealDeal.Services.Store.Infrastructure/StealDeal.Services.Store.Infrastructure.csproj", "Store/StealDeal.Services.Store.Infrastructure/"]

RUN dotnet restore "database/DatabaseMigrator/StealDeal.DatabaseMigrator.csproj"

COPY . .
RUN dotnet publish "database/DatabaseMigrator/StealDeal.DatabaseMigrator.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.DatabaseMigrator.dll"]

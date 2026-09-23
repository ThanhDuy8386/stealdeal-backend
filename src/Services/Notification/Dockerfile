FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5053
ENV ASPNETCORE_URLS=http://+:5053

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Notification.API/StealDeal.Services.Notification.API.csproj", "StealDeal.Services.Notification.API/"]
COPY ["StealDeal.Services.Notification.Application/StealDeal.Services.Notification.Application.csproj", "StealDeal.Services.Notification.Application/"]
COPY ["StealDeal.Services.Notification.Domain/StealDeal.Services.Notification.Domain.csproj", "StealDeal.Services.Notification.Domain/"]
COPY ["StealDeal.Services.Notification.Infrastructure/StealDeal.Services.Notification.Infrastructure.csproj", "StealDeal.Services.Notification.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Notification.API/StealDeal.Services.Notification.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Notification.API/StealDeal.Services.Notification.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Notification.API.dll"]

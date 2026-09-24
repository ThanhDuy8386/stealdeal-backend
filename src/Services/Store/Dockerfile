FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5169
ENV ASPNETCORE_URLS=http://+:5169

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Store.API/StealDeal.Services.Store.API.csproj", "StealDeal.Services.Store.API/"]
COPY ["StealDeal.Services.Store.Application/StealDeal.Services.Store.Application.csproj", "StealDeal.Services.Store.Application/"]
COPY ["StealDeal.Services.Store.Domain/StealDeal.Services.Store.Domain.csproj", "StealDeal.Services.Store.Domain/"]
COPY ["StealDeal.Services.Store.Infrastructure/StealDeal.Services.Store.Infrastructure.csproj", "StealDeal.Services.Store.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Store.API/StealDeal.Services.Store.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Store.API/StealDeal.Services.Store.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Store.API.dll"]

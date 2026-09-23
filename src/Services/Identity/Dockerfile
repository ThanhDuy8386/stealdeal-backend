FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5158
ENV ASPNETCORE_URLS=http://+:5158

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Identity.API/StealDeal.Services.Identity.API.csproj", "StealDeal.Services.Identity.API/"]
COPY ["StealDeal.Services.Identity.Application/StealDeal.Services.Identity.Application.csproj", "StealDeal.Services.Identity.Application/"]
COPY ["StealDeal.Services.Identity.Domain/StealDeal.Services.Identity.Domain.csproj", "StealDeal.Services.Identity.Domain/"]
COPY ["StealDeal.Services.Identity.Infrastructure/StealDeal.Services.Identity.Infrastructure.csproj", "StealDeal.Services.Identity.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Identity.API/StealDeal.Services.Identity.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Identity.API/StealDeal.Services.Identity.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Identity.API.dll"]

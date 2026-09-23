FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5165
ENV ASPNETCORE_URLS=http://+:5165

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Order.API/StealDeal.Services.Order.API.csproj", "StealDeal.Services.Order.API/"]
COPY ["StealDeal.Services.Order.Application/StealDeal.Services.Order.Application.csproj", "StealDeal.Services.Order.Application/"]
COPY ["StealDeal.Services.Order.Domain/StealDeal.Services.Order.Domain.csproj", "StealDeal.Services.Order.Domain/"]
COPY ["StealDeal.Services.Order.Infrastructure/StealDeal.Services.Order.Infrastructure.csproj", "StealDeal.Services.Order.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Order.API/StealDeal.Services.Order.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Order.API/StealDeal.Services.Order.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Order.API.dll"]

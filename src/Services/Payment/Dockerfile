FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5155
ENV ASPNETCORE_URLS=http://+:5155

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Payment.API/StealDeal.Services.Payment.API.csproj", "StealDeal.Services.Payment.API/"]
COPY ["StealDeal.Services.Payment.Application/StealDeal.Services.Payment.Application.csproj", "StealDeal.Services.Payment.Application/"]
COPY ["StealDeal.Services.Payment.Domain/StealDeal.Services.Payment.Domain.csproj", "StealDeal.Services.Payment.Domain/"]
COPY ["StealDeal.Services.Payment.Infrastructure/StealDeal.Services.Payment.Infrastructure.csproj", "StealDeal.Services.Payment.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Payment.API/StealDeal.Services.Payment.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Payment.API/StealDeal.Services.Payment.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Payment.API.dll"]

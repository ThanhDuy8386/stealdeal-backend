FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5185
ENV ASPNETCORE_URLS=http://+:5185

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["StealDeal.Services.Cart.API/StealDeal.Services.Cart.API.csproj", "StealDeal.Services.Cart.API/"]
COPY ["StealDeal.Services.Cart.Application/StealDeal.Services.Cart.Application.csproj", "StealDeal.Services.Cart.Application/"]
COPY ["StealDeal.Services.Cart.Domain/StealDeal.Services.Cart.Domain.csproj", "StealDeal.Services.Cart.Domain/"]
COPY ["StealDeal.Services.Cart.Infrastructure/StealDeal.Services.Cart.Infrastructure.csproj", "StealDeal.Services.Cart.Infrastructure/"]
RUN dotnet restore "StealDeal.Services.Cart.API/StealDeal.Services.Cart.API.csproj"

COPY . .
RUN dotnet publish "StealDeal.Services.Cart.API/StealDeal.Services.Cart.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StealDeal.Services.Cart.API.dll"]

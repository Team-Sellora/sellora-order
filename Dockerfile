FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Sellora.OrderService.Api/Sellora.OrderService.Api.csproj", "src/Sellora.OrderService.Api/"]
COPY ["src/Sellora.OrderService.Application/Sellora.OrderService.Application.csproj", "src/Sellora.OrderService.Application/"]
COPY ["src/Sellora.OrderService.Domain/Sellora.OrderService.Domain.csproj", "src/Sellora.OrderService.Domain/"]
COPY ["src/Sellora.OrderService.Infrastructure/Sellora.OrderService.Infrastructure.csproj", "src/Sellora.OrderService.Infrastructure/"]

RUN dotnet restore src/Sellora.OrderService.Api/Sellora.OrderService.Api.csproj

COPY src/ src/

RUN dotnet publish src/Sellora.OrderService.Api/Sellora.OrderService.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Sellora.OrderService.Api.dll"]

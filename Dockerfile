FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Sellora.Order.sln", "./"]
COPY ["src/Sellora.Order.Api/Sellora.Order.Api.csproj", "Sellora.Order.Api/"]
COPY ["src/Sellora.Order.Domain/Sellora.Order.Domain.csproj", "Sellora.Order.Domain/"]
COPY ["src/Sellora.Order.Infrastructure/Sellora.Order.Infrastructure.csproj", "Sellora.Order.Infrastructure/"]

RUN dotnet restore Sellora.Order.sln

COPY src/ .

RUN dotnet publish Sellora.Order.Api/Sellora.Order.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Sellora.Order.Api.dll"]

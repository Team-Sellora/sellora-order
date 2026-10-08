# Sellora Order Service

## Local configuration

`appsettings.json` holds no database password. For local runs, supply the
full connection string through user secrets (or the
`ConnectionStrings__Default` environment variable):

```bash
dotnet user-secrets --project src/Sellora.OrderService.Api set "ConnectionStrings:Default" \
  "Host=localhost;Port=5436;Database=order_db;Username=sellora;Password=<your local password>"
```

Deployed environments set `ConnectionStrings__Default` in their app settings.

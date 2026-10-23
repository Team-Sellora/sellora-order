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

## Load and performance testing

JMeter plans, Order and Notification testing scripts, the report and recorded
evidence are in [performance](performance/README.md). Open PowerShell in
`performance` when using its documented commands. Tokens are supplied through
environment variables and are not included in the repository.

The saved tests cover Order List, scheduled order creation and retrieval,
Notification List, and correlation of order events with notification records.
Results are from the October 2026 staging runs; they are not a current production
capacity guarantee. See the testing README for assumptions and remaining work.

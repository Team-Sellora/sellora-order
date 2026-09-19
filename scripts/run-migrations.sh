#!/usr/bin/env bash
set -euo pipefail

CONNECTION_STRING="${ConnectionStrings__Default:-Host=localhost;Port=5435;Database=order_db;Username=sellora;Password=sellora_dev_pass}"
export ConnectionStrings__Default="${CONNECTION_STRING}"

dotnet ef database update \
  --project src/Sellora.Order.Infrastructure/Sellora.Order.Infrastructure.csproj \
  --startup-project src/Sellora.Order.Api/Sellora.Order.Api.csproj

echo "Migrations applied successfully"

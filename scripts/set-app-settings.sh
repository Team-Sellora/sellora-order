#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="sellora-rg"
APP_NAME="sellora-order"

JWT_AUTHORITY="${JWT_AUTHORITY:?JWT_AUTHORITY environment variable is required}"
JWT_METADATA="${JWT_METADATA_ADDRESS:?JWT_METADATA_ADDRESS environment variable is required}"
JWT_ISSUER="${JWT_ISSUER:?JWT_ISSUER environment variable is required}"
JWT_AUDIENCE="${JWT_AUDIENCE:?JWT_AUDIENCE environment variable is required}"
PROD_CONNECTION_STRING="${PROD_CONNECTION_STRING:?PROD_CONNECTION_STRING environment variable is required}"
STAGING_CONNECTION_STRING="${STAGING_CONNECTION_STRING:?STAGING_CONNECTION_STRING environment variable is required}"

az webapp config appsettings set \
  --resource-group "${RESOURCE_GROUP}" \
  --name "${APP_NAME}" \
  --settings \
    Jwt__Authority="${JWT_AUTHORITY}" \
    Jwt__MetadataAddress="${JWT_METADATA}" \
    Jwt__Issuer="${JWT_ISSUER}" \
    Jwt__Audience="${JWT_AUDIENCE}" \
    ASPNETCORE_ENVIRONMENT="Production"

az webapp config connection-string set \
  --resource-group "${RESOURCE_GROUP}" \
  --name "${APP_NAME}" \
  --settings Default="${PROD_CONNECTION_STRING}" \
  --connection-string-type Custom

az webapp config appsettings set \
  --resource-group "${RESOURCE_GROUP}" \
  --name "${APP_NAME}" \
  --slot staging \
  --settings \
    Jwt__Authority="${JWT_AUTHORITY}" \
    Jwt__MetadataAddress="${JWT_METADATA}" \
    Jwt__Issuer="${JWT_ISSUER}" \
    Jwt__Audience="${JWT_AUDIENCE}" \
    ASPNETCORE_ENVIRONMENT="Staging"

az webapp config connection-string set \
  --resource-group "${RESOURCE_GROUP}" \
  --name "${APP_NAME}" \
  --slot staging \
  --settings Default="${STAGING_CONNECTION_STRING}" \
  --connection-string-type Custom

echo "App settings configured for production and staging slots"

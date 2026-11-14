#!/usr/bin/env bash
# Creates or updates the Module 3 environment (Flex Consumption, Australia East).
#
#   ./deploy.sh              starting state: connection strings and keys
#   ./deploy.sh identity     end state: managed identity for the queue and storage
#
# Override the defaults with environment variables:
#   RESOURCE_GROUP=rg-registrations-flex LOCATION=australiaeast ./deploy.sh
#
# To start again from nothing, delete the resource group:
#   az group delete --name rg-registrations-flex --yes
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-registrations-flex}"
LOCATION="${LOCATION:-australiaeast}"
USE_IDENTITY=false
if [ "${1:-}" = "identity" ]; then USE_IDENTITY=true; fi

cd "$(dirname "$0")"

echo "Resource group: $RESOURCE_GROUP ($LOCATION), managed identity: $USE_IDENTITY"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --name registrations-flex \
  --template-file main.bicep \
  --parameters useManagedIdentity="$USE_IDENTITY" \
  --query properties.outputs \
  --output jsonc

if [ "$USE_IDENTITY" = false ]; then
  echo
  echo "Next: add the DataverseConnection app setting to the function app (it holds the client secret)."
else
  echo
  echo "Next: create a Dataverse application user for the function app's identity and give it a security role."
fi

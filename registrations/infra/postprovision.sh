#!/usr/bin/env bash
set -euo pipefail

# Creates the Windows Consumption (Y1) Function App imperatively via `az
# functionapp create`. This is deliberately NOT done in main.bicep — ARM's
# preflight "validate" step for Dynamic-SKU plans has a known, intermittent
# bug (SubscriptionIsOverQuotaForSku, "Current Limit: 0 / Amount required: 0")
# that fails azd's provision flow before a real deployment is ever attempted,
# even though a live `az functionapp create` call succeeds immediately with
# the same SKU in the same subscription/region. See infra/main.bicep for
# details.
#
# Windows (not Linux) + code deployed as normal files (WEBSITE_RUN_FROM_PACKAGE=0,
# forced below) is what's required for the Azure portal's in-place code editor.

RESOURCE_GROUP="${AZURE_RESOURCE_GROUP}"
LOCATION="${AZURE_LOCATION}"
STORAGE_ACCOUNT="${AZURE_STORAGE_ACCOUNT_NAME}"
SERVICEBUS_NAMESPACE="${AZURE_SERVICEBUS_NAMESPACE}"
APPINSIGHTS_NAME="${AZURE_APPINSIGHTS_NAME}"
RESOURCE_TOKEN=$(az storage account show -g "$RESOURCE_GROUP" -n "$STORAGE_ACCOUNT" --query "name" -o tsv | sed 's/^st//')
FUNCTION_APP_NAME="func-registrations-${RESOURCE_TOKEN}"
DATAVERSE_ENVIRONMENT_URL="https://trek-dev.crm6.dynamics.com"

echo "Postprovision: ensuring Windows Consumption Function App '$FUNCTION_APP_NAME' exists..."

if az functionapp show -g "$RESOURCE_GROUP" -n "$FUNCTION_APP_NAME" >/dev/null 2>&1; then
  echo "Function App '$FUNCTION_APP_NAME' already exists — skipping create."
else
  # Node 22, not 24: durable-functions 2.1.4 (the last version with the classic,
  # function.json-triggered orchestrator API this app uses) calls the Node core
  # util.isDate() in its createTimer action, which Node 24 removed outright -
  # every orchestrator that calls context.df.createTimer throws on Node 24.
  az functionapp create \
    --resource-group "$RESOURCE_GROUP" \
    --name "$FUNCTION_APP_NAME" \
    --consumption-plan-location "$LOCATION" \
    --runtime node \
    --runtime-version 22 \
    --functions-version 4 \
    --storage-account "$STORAGE_ACCOUNT" \
    --app-insights "$APPINSIGHTS_NAME" \
    --assign-identity "[system]" \
    --https-only true \
    --tags "azd-env-name=${AZURE_ENV_NAME}" "azd-service-name=registrations" \
    -o none
  echo "Created Function App '$FUNCTION_APP_NAME'."
fi

echo "Ensuring key-based Service Bus auth rule on the 'registrations' queue..."
# Manage (not just Listen+Send) is required for target-based scaling: without it,
# the scale controller can't read queue length and falls back to slow incremental
# scaling. See https://learn.microsoft.com/azure/azure-functions/functions-target-based-scaling
az servicebus queue authorization-rule create \
  -g "$RESOURCE_GROUP" --namespace-name "$SERVICEBUS_NAMESPACE" \
  --queue-name registrations --name ingest-function --rights Listen Send Manage \
  -o none
SERVICEBUS_CONNECTION=$(az servicebus queue authorization-rule keys list \
  -g "$RESOURCE_GROUP" --namespace-name "$SERVICEBUS_NAMESPACE" \
  --queue-name registrations --name ingest-function \
  --query primaryConnectionString -o tsv)

echo "Configuring app settings (key-based Service Bus connection)..."
az functionapp config appsettings set \
  --resource-group "$RESOURCE_GROUP" \
  --name "$FUNCTION_APP_NAME" \
  --settings \
    "ServiceBusConnection=${SERVICEBUS_CONNECTION}" \
    "DATAVERSE_ENVIRONMENT_URL=${DATAVERSE_ENVIRONMENT_URL}" \
    "WEBSITE_RUN_FROM_PACKAGE=0" \
  -o none
az functionapp config appsettings delete \
  --resource-group "$RESOURCE_GROUP" \
  --name "$FUNCTION_APP_NAME" \
  --setting-names "ServiceBusConnection__fullyQualifiedNamespace" \
  -o none 2>/dev/null || true

# The managed-identity RBAC roles below are kept (harmless, no cost) so this
# app can be switched back to identity-based auth later just by re-adding the
# ServiceBusConnection__fullyQualifiedNamespace setting and removing
# ServiceBusConnection.
echo "Assigning Service Bus RBAC roles to the Function App's managed identity (kept for a future switch back to identity-based auth; not currently used)..."
PRINCIPAL_ID=$(az functionapp identity show -g "$RESOURCE_GROUP" -n "$FUNCTION_APP_NAME" --query principalId -o tsv)
SERVICEBUS_ID=$(az servicebus namespace show -g "$RESOURCE_GROUP" -n "$SERVICEBUS_NAMESPACE" --query id -o tsv)

az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "69a216fc-b8fb-44d8-bc22-1f3c2cd27a39" \
  --scope "$SERVICEBUS_ID" \
  -o none 2>/dev/null || echo "  (Service Bus Data Sender role already assigned)"

az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0" \
  --scope "$SERVICEBUS_ID" \
  -o none 2>/dev/null || echo "  (Service Bus Data Receiver role already assigned)"

FUNCTION_APP_URL="https://$(az functionapp show -g "$RESOURCE_GROUP" -n "$FUNCTION_APP_NAME" --query defaultHostName -o tsv)"

azd env set AZURE_FUNCTION_APP_NAME "$FUNCTION_APP_NAME"
azd env set FUNCTION_APP_URL "$FUNCTION_APP_URL"

echo "Function App ready: $FUNCTION_APP_URL"

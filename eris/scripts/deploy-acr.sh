#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR=${0:A:h}
ROOT_DIR=${SCRIPT_DIR:h}

AZURE_SUBSCRIPTION=${AZURE_SUBSCRIPTION:-"MVP Subscription"}
AZURE_RESOURCE_GROUP=${AZURE_RESOURCE_GROUP:-"rg-eris-prod"}
AZURE_LOCATION=${AZURE_LOCATION:-"eastus"}
ERIS_APP_NAME=${ERIS_APP_NAME:-"eris"}
ERIS_CONTAINER_APP_NAME=${ERIS_CONTAINER_APP_NAME:-"eris-api"}
ERIS_ADMIN_KEY=${ERIS_ADMIN_KEY:-"tardigrade"}
ERIS_IMAGE_TAG=${ERIS_IMAGE_TAG:-$(date -u +%Y%m%d%H%M%S)}

cd "$ROOT_DIR"

az account set --subscription "$AZURE_SUBSCRIPTION"
az group create --name "$AZURE_RESOURCE_GROUP" --location "$AZURE_LOCATION" --output none

bootstrap_deployment="${ERIS_APP_NAME}-infra-${ERIS_IMAGE_TAG}"

az deployment group create \
  --name "$bootstrap_deployment" \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --template-file "$ROOT_DIR/infra/main.bicep" \
  --parameters @"$ROOT_DIR/infra/main.parameters.json" \
  --parameters location="$AZURE_LOCATION" \
  --parameters appName="$ERIS_APP_NAME" \
  --parameters containerAppName="$ERIS_CONTAINER_APP_NAME" \
  --parameters adminKey="$ERIS_ADMIN_KEY" \
  --parameters containerImage="" \
  --output none

acr_name=$(az deployment group show \
  --name "$bootstrap_deployment" \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --query properties.outputs.acrName.value \
  --output tsv)

acr_login_server=$(az deployment group show \
  --name "$bootstrap_deployment" \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --query properties.outputs.acrLoginServer.value \
  --output tsv)

az acr build \
  --registry "$acr_name" \
  --image "${ERIS_CONTAINER_APP_NAME}:${ERIS_IMAGE_TAG}" \
  --file "$ROOT_DIR/Eris.Api/Dockerfile" \
  "$ROOT_DIR"

container_image="${acr_login_server}/${ERIS_CONTAINER_APP_NAME}:${ERIS_IMAGE_TAG}"
app_deployment="${ERIS_APP_NAME}-app-${ERIS_IMAGE_TAG}"

az deployment group create \
  --name "$app_deployment" \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --template-file "$ROOT_DIR/infra/main.bicep" \
  --parameters @"$ROOT_DIR/infra/main.parameters.json" \
  --parameters location="$AZURE_LOCATION" \
  --parameters appName="$ERIS_APP_NAME" \
  --parameters containerAppName="$ERIS_CONTAINER_APP_NAME" \
  --parameters adminKey="$ERIS_ADMIN_KEY" \
  --parameters containerImage="$container_image" \
  --query properties.outputs.containerAppUrl.value \
  --output tsv
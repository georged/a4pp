# Deployment Plan — Registration Functions

**Status:** In Progress

## Summary
Two JavaScript Azure Functions (HTTP trigger → Service Bus → Dataverse) for dehydrating registration events into `contact` and `tot_registration` Dataverse tables. Separate from the Eris project.

## Azure Context
- **Subscription:** MVP (`2c966a60-fe5e-4d73-817b-058a7fbde198`)
- **Location:** eastus
- **Resource Group:** rg-registrations (created by azd)

## Architecture
```
HTTP POST /api/ingest
    ↓
HttpIngestFunction  (HTTP trigger)
    ↓ sends message
Service Bus Queue: registrations
    ↓ triggers
ProcessRegistrationFunction  (Service Bus trigger)
    ↓ upsert
Dataverse: contact + tot_registration
```

## Components
| Component | Azure Service |
|-----------|--------------|
| ingestRegistration (HttpIngestFunction) | Azure Functions (Node.js 24, Windows Consumption / Y1) |
| ProcessRegistrationFunction | Not yet implemented |
| Message queue | Service Bus Standard + queue `registrations` |
| Runtime storage | Storage Account (Standard LRS, key-based access) |
| Auth to Azure resources | Service Bus: key-based (queue-scoped SAS rule `ingest-function`, `ServiceBusConnection` app setting). Storage: account key. Managed identity RBAC roles are still granted on the namespace but unused — kept so it can be switched back to identity-based later. |
| Auth to Dataverse | Application User (manual post-deploy step) |

**Hosting note:** switched from Linux Flex Consumption to classic Windows
Consumption specifically so the function code can be edited in the Azure
portal without redeploying (the portal's in-place editor is unavailable on
Linux, and disabled whenever code is "run from package" — `WEBSITE_RUN_FROM_PACKAGE`
is explicitly forced to `0`).

**Provisioning note:** the Function App + its Y1 plan are created by
`infra/postprovision.sh` (an azd `postprovision` hook) via `az functionapp
create`, not by `main.bicep`. ARM's preflight "validate" step for Dynamic-SKU
plans has a known, intermittent bug (`SubscriptionIsOverQuotaForSku` with
"Current Limit: 0 / Amount required: 0") that fails `azd provision` before a
real deployment is ever attempted — even though a live `az functionapp
create` call with the identical SKU succeeds immediately in the same
subscription/region. Bicep still owns Storage, Application Insights, and
Service Bus, which aren't affected.

## Field Mappings

### contact
| Input | Dataverse |
|-------|-----------|
| customer.firstName | firstname |
| customer.lastName | lastname |
| customer.email | emailaddress1 |

### tot_registration
| Input | Dataverse |
|-------|-----------|
| registrationId | tot_externalid (idempotency key) |
| sourceSystem | tot_sourcesystem |
| eventId | tot_eventid |
| eventTime | tot_submittedon |
| campaignCode | tot_campaigncode |
| ticketType | tot_tickettype |
| quantity | tot_quantity |
| totalAmount | tot_totalamount |
| (contact id) | tot_contact (lookup) |

## Infrastructure Files
- `infra/main.bicep` — All Azure resources
- `infra/main.parameters.json` — Parameter file with azd token substitution
- `azure.yaml` — azd service definition

## Idempotency
- Contact: query by `emailaddress1` before create; reuse existing if found
- Registration: query by `tot_externalid` before create; skip if found

## Post-Deploy Manual Steps
1. In Dataverse (`trek-dev.crm6.dynamics.com`) → Settings → Users → Application Users
2. Create Application User with the Function App's Managed Identity Object ID
3. Assign a security role with Create/Read on `contact` and `tot_registration`
4. Set app setting `DATAVERSE_ENVIRONMENT_URL` = `https://trek-dev.crm6.dynamics.com`

## Execution Checklist
- [x] Functions project created
- [x] Bicep infrastructure created
- [x] azure.yaml created
- [x] Build verified
- [x] Provisioned to Azure (`rg-ingest`, East US, subscription MVP)
- [x] `ingestRegistration` deployed and smoke-tested end-to-end (HTTP → Service Bus)
- [ ] ProcessRegistrationFunction (Service Bus trigger → Dataverse) — not yet implemented
- [ ] Dataverse Application User post-deploy step (see above)

## Deployed Resources (env: `ingest`)
- Resource group: `rg-ingest`
- Function App: `func-registrations-vtydwfxhlb6j4` — https://func-registrations-vtydwfxhlb6j4.azurewebsites.net
- Service Bus namespace: `sb-vtydwfxhlb6j4`, queue `registrations`
- Storage account: `stvtydwfxhlb6j4`
- Application Insights: `appi-vtydwfxhlb6j4`

# Deployment Plan — Registration Functions

**Status:** In Progress

## Summary
Two JavaScript Azure Functions (HTTP trigger → Service Bus → Dataverse) for dehydrating registration events into `contact` and `tot_registration` Dataverse tables. Separate from the Eris project.

## Azure Context
- **Subscription:** MVP Subscription (`96f4a41d-717b-42bc-9a2a-43fbc993c250`)
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
| HttpIngestFunction | Azure Functions (Node.js 20 Flex Consumption) |
| ProcessRegistrationFunction | Azure Functions (Node.js 20 Flex Consumption) |
| Message queue | Service Bus Standard + queue `registrations` |
| Runtime storage | Storage Account (Standard LRS) |
| Auth to Azure resources | System-assigned Managed Identity |
| Auth to Dataverse | Application User (manual post-deploy step) |

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
- [ ] Ready for Validation

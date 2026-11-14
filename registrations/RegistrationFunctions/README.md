# RegistrationFunctions: the Module 3 function app

The C# (.NET 10, isolated worker) equivalent of its sibling `RegistrationFunctionsJs` at the
`m2-batches` tag: HTTP ingest to a Service Bus queue, then batches of 20 into Dataverse as deep inserts.

This is the Module 3 managed-identity state. Dataverse uses `DefaultAzureCredential` through
the `ServiceClient` token-provider callback; Service Bus and host storage use identity-based
Functions settings. No application client secret or Azure service access key is required.

## Run it locally

Open this folder (`registrations/RegistrationFunctions`) in VS Code and install the recommended extensions when prompted.

1. Install the .NET 10 SDK and Azure Functions Core Tools v4.
2. Copy `local.settings.sample.json` to `local.settings.json` and fill in `DataverseUrl` (the
   environment root URL, not an API URL) and `ServiceBusConnection__fullyQualifiedNamespace`
   (the namespace hostname, without `sb://`).
3. Create a queue named `registrations-flex` in your Service Bus namespace, or run
   `../infra-flex/deploy.sh identity` to create the identity-based environment.
4. Start Azurite (command palette: "Azurite: Start").
5. Run `az login --tenant <your-tenant-id>` using an account with access to the Dataverse
   environment and the Service Bus Data Sender and Data Receiver roles on the namespace.
   Local execution uses your developer identity, not the function app's managed identity.
6. Press F5. Post `sample-input.json` from the repo root to `http://localhost:7071/api/ingestRegistration`.

If reusing an existing local settings file, remove `DataverseConnection` and
`ServiceBusConnection`; the latter takes precedence over the identity-based settings.
`AzureWebJobsStorage=UseDevelopmentStorage=true` is only for local Azurite, not Azure.

Set a breakpoint in `ProcessRegistration.Run` to step through a batch.

## Azure configuration

Enable the function app's system-assigned managed identity and configure these app settings:

| Setting | Value |
|---|---|
| `DataverseUrl` | `https://<your-org>.crm6.dynamics.com` |
| `ServiceBusConnection__fullyQualifiedNamespace` | `<your-namespace>.servicebus.windows.net` |
| `AzureWebJobsStorage__accountName` | Host storage account name |

The existing Service Bus input and output bindings resolve the `ServiceBusConnection` prefix
automatically. No binding attribute changes are needed.

The identity needs:

- **Azure Service Bus Data Sender** and **Azure Service Bus Data Receiver** on the namespace.
- **Storage Blob Data Owner** on host storage for this app's HTTP and Service Bus triggers.
- A **Dataverse application user** for the managed identity, with a security role granting the
  required create and relationship privileges for `contact` and `tot_registration`.
  Azure RBAC alone does not grant Dataverse access.

For Flex Consumption, deployment storage authentication must also use
`SystemAssignedIdentity`, not `StorageAccountConnectionString`. The existing
`../infra-flex/deploy.sh identity` configures this, the Azure settings (except `DataverseUrl`),
and the Azure role assignments. It replaces app settings, so set `DataverseUrl` afterwards.

Remove the old `DataverseConnection`, `ServiceBusConnection`, `AzureWebJobsStorage`, and
`DEPLOYMENT_STORAGE_CONNECTION_STRING` settings from Azure after switching to the identity
settings. `DefaultAzureCredential` uses the system-assigned identity in Azure and can use
your Azure CLI login locally; remove any old `AZURE_CLIENT_SECRET` credential configuration
so it cannot take precedence. Deploy this C# project from VS Code; the parent `azure.yaml`
still targets the separate JavaScript app.

`APPLICATIONINSIGHTS_CONNECTION_STRING` is telemetry endpoint configuration, not an
Azure service access key, and can remain. The HTTP endpoint still uses function-key
authorization; managed identity replaces outbound authentication, not inbound API security.

## What changed from the JavaScript version

| JavaScript (`m2-batches`) | C# |
|---|---|
| Hand-written token request and cache | `ServiceClient` with `DefaultAzureCredential` |
| Hand-built multipart `$batch` body | `ExecuteMultipleRequest` |
| `while (status === 429)` retry loop | Built into `ServiceClient` |
| Navigation property JSON for deep insert | `RelatedEntities` |
| `function.json` per function | Attributes on the method |

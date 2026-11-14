# RegistrationFunctions: the Module 3 function app

The C# (.NET 10, isolated worker) equivalent of its sibling `RegistrationFunctionsJs` at the
`m2-batches` tag: HTTP ingest to a Service Bus queue, then batches of 20 into Dataverse as deep inserts.

This is the Module 3 starting state. It still signs in with an app registration and connection
strings; the switch to managed identity is the second half of the module.

## Run it locally

Open this folder (`registrations/RegistrationFunctions`) in VS Code and install the recommended extensions when prompted.

1. Install the .NET 10 SDK and Azure Functions Core Tools v4.
2. Copy `local.settings.sample.json` to `local.settings.json` and fill in the two connections.
3. Create a queue named `registrations-flex` in your Service Bus namespace, or run `../infra-flex/deploy.sh` to create the whole environment.
4. Start Azurite (command palette: "Azurite: Start").
5. Press F5. Post `sample-input.json` from the repo root to `http://localhost:7071/api/ingestRegistration`.

Set a breakpoint in `ProcessRegistration.Run` to step through a batch.

## What changed from the JavaScript version

| JavaScript (`m2-batches`) | C# |
|---|---|
| Hand-written token request and cache | `ServiceClient` |
| Hand-built multipart `$batch` body | `ExecuteMultipleRequest` |
| `while (status === 429)` retry loop | Built into `ServiceClient` |
| Navigation property JSON for deep insert | `RelatedEntities` |
| `function.json` per function | Attributes on the method |

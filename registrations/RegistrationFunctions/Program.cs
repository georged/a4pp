using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// One Dataverse client for the whole app. It signs in, caches and refreshes the token,
// and retries on 429 using Retry-After, so none of that lives in our code any more.
// Module 3, part 1: still an app registration and a client secret (see local.settings.sample.json).
builder.Services.AddSingleton<IOrganizationServiceAsync2>(_ =>
    new ServiceClient(builder.Configuration["DataverseConnection"]));

builder.Build().Run();

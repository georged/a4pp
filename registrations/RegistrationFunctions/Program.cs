using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// One Dataverse client for the whole app. It signs in, caches and refreshes the token,
// and retries on 429 using Retry-After, so none of that lives in our code any more.
var credential = new DefaultAzureCredential();
var dataverseUrl = new Uri(builder.Configuration["DataverseUrl"]!);

builder.Services.AddSingleton<IOrganizationServiceAsync2>(_ =>
    new ServiceClient(dataverseUrl, async url =>
    {
        var scope = new Uri(url).GetLeftPart(UriPartial.Authority) + "/.default";
        return (await credential.GetTokenAsync(new TokenRequestContext([scope]))).Token;
    }));

builder.Build().Run();

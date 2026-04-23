var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Eris_Api>("eris-api")
	.WithExternalHttpEndpoints();

builder.AddProject<Projects.Hermes_App>("hermes-app")
	.WithExternalHttpEndpoints();

builder.Build().Run();

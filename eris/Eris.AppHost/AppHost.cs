var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Eris_Api>("eris-api")
	.WithExternalHttpEndpoints();

builder.Build().Run();

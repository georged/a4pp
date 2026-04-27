using Eris.Api.Data;
using Eris.Api.Hubs;
using Eris.Api.Models;
using Eris.Api.Services;
using Eris.Api.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
const string hiddenRegistrationPrefix = "__direct__::";

builder.AddServiceDefaults();

builder.Services.AddDbContextFactory<ErisDbContext>(options =>
	options.UseSqlite("Data Source=eris.db"));
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("delivery", client =>
{
	client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped(sp =>
{
	var navigation = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
	return new HttpClient { BaseAddress = new Uri(navigation.BaseUri) };
});

builder.Services.AddSingleton<EventGenerator>();
builder.Services.AddSingleton<DeliveryEngine>();
builder.Services.AddSingleton<SimulationProgressPublisher>();
builder.Services.AddSingleton<ScenarioRunner>();

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ErisDbContext>>().CreateDbContextAsync())
{
	await db.Database.EnsureCreatedAsync();

	try
	{
		await db.Database.ExecuteSqlRawAsync("SELECT OwnerKey FROM Registrations LIMIT 1");
	}
	catch (SqliteException)
	{
		await db.Database.EnsureDeletedAsync();
		await db.Database.EnsureCreatedAsync();
	}

	var existingOwnerKeys = await db.Registrations
		.Select(x => x.OwnerKey)
		.Distinct()
		.ToListAsync();
	SessionKeyAllocator.Seed(existingOwnerKeys);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapHub<SimulationHub>("/hubs/simulation");
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.MapGet("/api/me", (HttpContext httpContext) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	return Results.Ok(new { studentKey });
});

app.MapGet("/api/registrations", async (HttpContext httpContext, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	return await db.Registrations
		.Where(x => x.OwnerKey == studentKey && !x.Name.StartsWith(hiddenRegistrationPrefix))
		.OrderBy(x => x.CreatedAtUtc)
		.ToListAsync(ct);
});

app.MapPost("/api/registrations/resolve", async (HttpContext httpContext, ResolveRegistrationRequest request, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);

	if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var endpointUri)
		|| (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
	{
		return Results.BadRequest("EndpointUrl must be a valid http/https URL.");
	}

	var normalizedUrl = request.EndpointUrl.Trim();

	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var existing = await db.Registrations
		.FirstOrDefaultAsync(x => x.OwnerKey == studentKey && x.EndpointUrl == normalizedUrl, ct);

	if (existing is not null)
	{
		return Results.Ok(new { id = existing.Id });
	}

	var registration = new RegistrationEndpoint
	{
		Id = Guid.NewGuid(),
		OwnerKey = studentKey,
		Name = $"{hiddenRegistrationPrefix}{endpointUri.Host}",
		EndpointUrl = normalizedUrl,
		CreatedAtUtc = DateTime.UtcNow
	};

	db.Registrations.Add(registration);
	await db.SaveChangesAsync(ct);
	return Results.Ok(new { id = registration.Id });
});

app.MapPost("/api/registrations", async (HttpContext httpContext, CreateRegistrationRequest request, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);

	if (string.IsNullOrWhiteSpace(request.Name))
	{
		return Results.BadRequest("Name is required.");
	}

	if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var endpointUri)
		|| (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
	{
		return Results.BadRequest("EndpointUrl must be a valid http/https URL.");
	}

	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var registration = new RegistrationEndpoint
	{
		Id = Guid.NewGuid(),
		OwnerKey = studentKey,
		Name = request.Name.Trim(),
		EndpointUrl = request.EndpointUrl.Trim(),
		CreatedAtUtc = DateTime.UtcNow
	};

	db.Registrations.Add(registration);
	await db.SaveChangesAsync(ct);
	return Results.Created($"/api/registrations/{registration.Id}", registration);
});

app.MapDelete("/api/registrations/{id:guid}", async (HttpContext httpContext, Guid id, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var registration = await db.Registrations.FirstOrDefaultAsync(x => x.Id == id && x.OwnerKey == studentKey, ct);
	if (registration is null)
	{
		return Results.NotFound();
	}

	db.Registrations.Remove(registration);
	await db.SaveChangesAsync(ct);
	return Results.NoContent();
});

app.MapPost("/api/registrations/{id:guid}/simulate", async (HttpContext httpContext, Guid id, StartSimulationRequest request, IDbContextFactory<ErisDbContext> dbFactory, ScenarioRunner runner, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var owned = await db.Registrations.AnyAsync(x => x.Id == id && x.OwnerKey == studentKey, ct);
	if (!owned)
	{
		return Results.NotFound("Registration not found.");
	}

	try
	{
		var runId = await runner.StartRunAsync(id, request, ct);
		return Results.Accepted($"/api/simulations/{runId}", new { runId });
	}
	catch (InvalidOperationException ex)
	{
		return Results.NotFound(ex.Message);
	}
});

app.MapGet("/api/simulations/{runId:guid}", async (HttpContext httpContext, Guid runId, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var run = await db.SimulationRuns.FirstOrDefaultAsync(x => x.Id == runId, ct);
	if (run is null)
	{
		return Results.NotFound();
	}

	var owned = await db.Registrations.AnyAsync(x => x.Id == run.RegistrationEndpointId && x.OwnerKey == studentKey, ct);
	if (!owned)
	{
		return Results.NotFound();
	}

	var failureRate = run.Total == 0 ? 0 : (double)run.Failed / run.Total * 100;
	return Results.Ok(new
	{
		run.Id,
		run.RegistrationEndpointId,
		run.Scenario,
		run.StartedAtUtc,
		run.CompletedAtUtc,
		run.Total,
		run.Succeeded,
		run.Failed,
		run.IsRunning,
		FailureRate = failureRate
	});
});

app.MapGet("/api/registrations/{id:guid}/history", async (HttpContext httpContext, Guid id, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var owned = await db.Registrations.AnyAsync(x => x.Id == id && x.OwnerKey == studentKey, ct);
	if (!owned)
	{
		return Results.NotFound();
	}

	var history = await db.EventRecords
		.Where(x => x.RegistrationEndpointId == id)
		.OrderByDescending(x => x.SentAtUtc)
		.Take(200)
		.ToListAsync(ct);

	return Results.Ok(history);
});

app.MapDelete("/api/registrations/{id:guid}/history", async (HttpContext httpContext, Guid id, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var owned = await db.Registrations.AnyAsync(x => x.Id == id && x.OwnerKey == studentKey, ct);
	if (!owned)
	{
		return Results.NotFound();
	}

	var records = await db.EventRecords
		.Where(x => x.RegistrationEndpointId == id)
		.ToListAsync(ct);

	if (records.Count > 0)
	{
		db.EventRecords.RemoveRange(records);
		await db.SaveChangesAsync(ct);
	}

	return Results.NoContent();
});

app.MapGet("/api/registrations/{id:guid}/stats", async (HttpContext httpContext, Guid id, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	var studentKey = ResolveStudentKey(httpContext);
	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var owned = await db.Registrations.AnyAsync(x => x.Id == id && x.OwnerKey == studentKey, ct);
	if (!owned)
	{
		return Results.NotFound();
	}

	var total = await db.EventRecords.CountAsync(x => x.RegistrationEndpointId == id, ct);
	var failed = await db.EventRecords.CountAsync(x => x.RegistrationEndpointId == id && !x.IsSuccess, ct);
	var succeeded = total - failed;
	var failureRate = total == 0 ? 0 : (double)failed / total * 100;

	return Results.Ok(new
	{
		RegistrationId = id,
		Total = total,
		Succeeded = succeeded,
		Failed = failed,
		FailureRate = failureRate
	});
});

app.MapGet("/api/admin/registrations", async (HttpContext httpContext, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	if (!TryAuthorizeAdmin(app.Configuration, httpContext, httpContext.Request, out var unauthorizedResult))
	{
		return unauthorizedResult!;
	}

	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var registrations = await db.Registrations.OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
	var stats = await db.EventRecords
		.GroupBy(x => x.RegistrationEndpointId)
		.Select(g => new
		{
			RegistrationId = g.Key,
			Total = g.Count(),
			Failed = g.Count(x => !x.IsSuccess)
		})
		.ToDictionaryAsync(x => x.RegistrationId, ct);

	var runsByRegistration = await db.SimulationRuns
		.OrderByDescending(x => x.StartedAtUtc)
		.ToListAsync(ct);

	var result = registrations.Select(r =>
	{
		stats.TryGetValue(r.Id, out var value);
		var total = value?.Total ?? 0;
		var failed = value?.Failed ?? 0;
		var succeeded = total - failed;

		var runs = runsByRegistration
			.Where(x => x.RegistrationEndpointId == r.Id)
			.Select(x => new AdminRunSummary
			{
				RunId = x.Id,
				Scenario = x.Scenario,
				StartedAtUtc = x.StartedAtUtc,
				CompletedAtUtc = x.CompletedAtUtc,
				Total = x.Total,
				Succeeded = x.Succeeded,
				Failed = x.Failed,
				FailureRate = x.Total == 0 ? 0 : (double)x.Failed / x.Total * 100,
				IsRunning = x.IsRunning
			})
			.ToList();

		return new AdminRegistrationDetails
		{
			Id = r.Id,
			Name = r.Name,
			EndpointUrl = r.EndpointUrl,
			OwnerKey = r.OwnerKey,
			CreatedAtUtc = r.CreatedAtUtc,
			Total = total,
			Succeeded = succeeded,
			Failed = failed,
			FailureRate = total == 0 ? 0 : (double)failed / total * 100,
			Runs = runs
		};
	});

	return Results.Ok(result);
});

app.MapDelete("/api/admin/registrations/{id:guid}", async (HttpContext httpContext, Guid id, IDbContextFactory<ErisDbContext> dbFactory, CancellationToken ct) =>
{
	if (!TryAuthorizeAdmin(app.Configuration, httpContext, httpContext.Request, out var unauthorizedResult))
	{
		return unauthorizedResult!;
	}

	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var registration = await db.Registrations.FirstOrDefaultAsync(x => x.Id == id, ct);
	if (registration is null)
	{
		return Results.NotFound();
	}

	db.Registrations.Remove(registration);
	await db.SaveChangesAsync(ct);
	return Results.NoContent();
});

app.MapPost("/api/admin/registrations/bulk-delete", async (
	HttpContext httpContext,
	BulkDeleteWebhooksRequest request,
	IDbContextFactory<ErisDbContext> dbFactory,
	CancellationToken ct) =>
{
	if (!TryAuthorizeAdmin(app.Configuration, httpContext, httpContext.Request, out var unauthorizedResult))
	{
		return unauthorizedResult!;
	}

	if (request.RegistrationIds.Count == 0)
	{
		return Results.BadRequest("At least one registration id is required.");
	}

	await using var db = await dbFactory.CreateDbContextAsync(ct);
	var registrations = await db.Registrations
		.Where(x => request.RegistrationIds.Contains(x.Id))
		.ToListAsync(ct);

	if (registrations.Count == 0)
	{
		return Results.Ok(new { deleted = 0 });
	}

	db.Registrations.RemoveRange(registrations);
	await db.SaveChangesAsync(ct);
	return Results.Ok(new { deleted = registrations.Count });
});

app.MapPost("/api/admin/auth/validate", (HttpContext httpContext) =>
{
	if (!TryAuthorizeAdmin(app.Configuration, httpContext, httpContext.Request, out var unauthorizedResult))
	{
		return unauthorizedResult!;
	}

	return Results.Ok(new { ok = true });
});

app.Run();

static string ResolveStudentKey(HttpContext httpContext)
{
	const string cookieName = "eris-student-key";
	const string headerName = "X-Eris-Student-Key";

	void PersistCookie(string key)
	{
		httpContext.Response.Cookies.Append(cookieName, key, new CookieOptions
		{
			HttpOnly = true,
			IsEssential = true,
			SameSite = SameSiteMode.Lax,
			Secure = httpContext.Request.IsHttps,
			Expires = DateTimeOffset.UtcNow.AddDays(30)
		});
	}

	var headerKey = httpContext.Request.Headers[headerName].FirstOrDefault();
	if (!string.IsNullOrWhiteSpace(headerKey))
	{
		return SessionKeyAllocator.Claim(headerKey.Trim());
	}

	if (httpContext.Request.Cookies.TryGetValue(cookieName, out var cookieKey) && !string.IsNullOrWhiteSpace(cookieKey))
	{
		var claimed = SessionKeyAllocator.Claim(cookieKey.Trim());
		if (!string.Equals(cookieKey, claimed, StringComparison.OrdinalIgnoreCase))
		{
			PersistCookie(claimed);
		}

		return claimed;
	}

	var generated = SessionKeyAllocator.Claim(null);
	PersistCookie(generated);

	return generated;
}

static bool TryAuthorizeAdmin(IConfiguration configuration, HttpContext? httpContext, HttpRequest? request, out IResult? unauthorizedResult)
{
	var configured = configuration["Admin:Key"];
	if (string.IsNullOrWhiteSpace(configured))
	{
		configured = "change-me-admin-key";
	}

	var headerValue = request?.Headers["X-Eris-Admin-Key"].FirstOrDefault()
		?? httpContext?.Request.Headers["X-Eris-Admin-Key"].FirstOrDefault();

	if (string.Equals(headerValue, configured, StringComparison.Ordinal))
	{
		unauthorizedResult = null;
		return true;
	}

	unauthorizedResult = Results.Unauthorized();
	return false;
}

static class SessionKeyAllocator
{
	private static readonly object Sync = new();
	private static readonly Random Random = new();
	private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> RequestedToAssigned = new(StringComparer.OrdinalIgnoreCase);

	private static readonly string[] Animals =
	[
		"aardvark","albatross","alligator","alpaca","antelope","armadillo","axolotl","badger","bandicoot","barnowl",
		"barracuda","beaver","bison","blackbird","bluejay","boar","bobcat","bonobo","buffalo","bulldog",
		"camel","capybara","caracal","cardinal","cassowary","catfish","chameleon","cheetah","chinchilla","chipmunk",
		"cobra","condor","cougar","coyote","crab","crane","crocodile","crow","deer","dingo",
		"dolphin","donkey","dragonfly","duck","eagle","echidna","egret","elephant","elk","falcon",
		"ferret","finch","flamingo","fox","frog","gazelle","gecko","gerbil","giraffe","gnu",
		"goose","gorilla","goshawk","hamster","hare","hedgehog","heron","hippo","hornet","hummingbird",
		"hyena","ibex","iguana","jackal","jaguar","kangaroo","kingfisher","koala","lemur","leopard",
		"lion","lynx","macaw","magpie","manatee","meerkat","mink","moose","narwhal","newt",
		"ocelot","octopus","opossum","orangutan","orca","osprey","otter","owl","panda","panther"
	];

	public static void Seed(IEnumerable<string> keys)
	{
		lock (Sync)
		{
			foreach (var key in keys)
			{
				if (!string.IsNullOrWhiteSpace(key))
				{
					var normalized = key.Trim();
					Reserved.Add(normalized);
					RequestedToAssigned[normalized] = normalized;
				}
			}
		}
	}

	public static string Claim(string? requested)
	{
		lock (Sync)
		{
			if (!string.IsNullOrWhiteSpace(requested))
			{
				var normalized = requested.Trim();
				if (RequestedToAssigned.TryGetValue(normalized, out var alreadyAssigned))
				{
					return alreadyAssigned;
				}

				if (IsKnownAnimal(normalized) && !Reserved.Contains(normalized))
				{
					Reserved.Add(normalized);
					RequestedToAssigned[normalized] = normalized;
					return normalized;
				}

				var mapped = AllocateNewKey();
				RequestedToAssigned[normalized] = mapped;
				RequestedToAssigned[mapped] = mapped;
				return mapped;
			}

			return AllocateNewKey();
		}
	}

	private static string AllocateNewKey()
	{
		var available = Animals.Where(x => !Reserved.Contains(x)).ToList();
		if (available.Count == 0)
		{
			var fallback = $"otter-{Guid.NewGuid():N}";
			Reserved.Add(fallback);
			return fallback;
		}

		var chosen = available[Random.Next(available.Count)];
		Reserved.Add(chosen);
		return chosen;
	}

	private static bool IsKnownAnimal(string key)
	{
		return Animals.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
	}
}

using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var config = new HermesBehaviorConfig();
var events = new ConcurrentQueue<HermesEvent>();
var random = new Random();

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(events);
builder.Services.AddSingleton(random);
builder.Services.AddHttpClient("eris", client =>
{
	client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (HermesBehaviorConfig cfg) => Results.Ok(cfg));

app.MapPost("/api/config", (HermesBehaviorConfig cfg, HermesConfigUpdate update) =>
{
	if (update.DropPercent is < 0 or > 100 || update.FailPercent is < 0 or > 100)
	{
		return Results.BadRequest("dropPercent and failPercent must be between 0 and 100.");
	}

	cfg.DropPercent = update.DropPercent;
	cfg.FailPercent = update.FailPercent;
	return Results.Ok(cfg);
});

app.MapGet("/api/events", (ConcurrentQueue<HermesEvent> queue) =>
{
	return Results.Ok(queue.OrderByDescending(x => x.ReceivedAtUtc).Take(500));
});

app.MapPost("/api/callback", async (HttpContext context, HermesBehaviorConfig cfg, ConcurrentQueue<HermesEvent> queue, Random rng) =>
{
	using var reader = new StreamReader(context.Request.Body);
	var payload = await reader.ReadToEndAsync();

	var roll = rng.NextDouble() * 100;
	if (roll < cfg.DropPercent)
	{
		// Simulate dropped callback by delaying longer than Eris sender timeout.
		await Task.Delay(TimeSpan.FromSeconds(25));
		return Results.NoContent();
	}

	if (roll < cfg.DropPercent + cfg.FailPercent)
	{
		return Results.StatusCode(StatusCodes.Status500InternalServerError);
	}

	queue.Enqueue(new HermesEvent
	{
		Id = Guid.NewGuid(),
		ReceivedAtUtc = DateTime.UtcNow,
		PayloadJson = payload,
		Headers = context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString())
	});

	while (queue.Count > 2000 && queue.TryDequeue(out _))
	{
	}

	return Results.Ok(new { accepted = true });
});

app.MapPost("/api/register-with-eris", async (
	RegisterWithErisRequest request,
	HttpContext context,
	IHttpClientFactory httpClientFactory,
	CancellationToken ct) =>
{
	if (!Uri.TryCreate(request.ErisBaseUrl, UriKind.Absolute, out var erisBase)
		|| (erisBase.Scheme != Uri.UriSchemeHttp && erisBase.Scheme != Uri.UriSchemeHttps))
	{
		return Results.BadRequest("erisBaseUrl must be a valid http/https URL.");
	}

	var callbackUrl = $"{context.Request.Scheme}://{context.Request.Host}/api/callback";
	var payload = new
	{
		name = string.IsNullOrWhiteSpace(request.Name) ? "Hermes Receiver" : request.Name.Trim(),
		endpointUrl = callbackUrl
	};

	var client = httpClientFactory.CreateClient("eris");
	using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(erisBase, "/api/registrations"))
	{
		Content = JsonContent.Create(payload)
	};

	if (!string.IsNullOrWhiteSpace(request.StudentKey))
	{
		httpRequest.Headers.Add("X-Eris-Student-Key", request.StudentKey.Trim());
	}

	using var response = await client.SendAsync(httpRequest, ct);
	var content = await response.Content.ReadAsStringAsync(ct);
	if (!response.IsSuccessStatusCode)
	{
		return Results.BadRequest(content);
	}

	var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
	return Results.Ok(obj);
});

app.Run();

public class HermesBehaviorConfig
{
	public int DropPercent { get; set; }
	public int FailPercent { get; set; }
}

public class HermesEvent
{
	public Guid Id { get; set; }
	public DateTime ReceivedAtUtc { get; set; }
	public string PayloadJson { get; set; } = string.Empty;
	public Dictionary<string, string> Headers { get; set; } = new();
}

public class RegisterWithErisRequest
{
	public string ErisBaseUrl { get; set; } = string.Empty;
	public string Name { get; set; } = "Hermes Receiver";
	public string? StudentKey { get; set; }
}

public record HermesConfigUpdate(int DropPercent, int FailPercent);

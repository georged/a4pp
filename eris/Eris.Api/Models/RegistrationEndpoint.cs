namespace Eris.Api.Models;

public class RegistrationEndpoint
{
    public Guid Id { get; set; }
    public string OwnerKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

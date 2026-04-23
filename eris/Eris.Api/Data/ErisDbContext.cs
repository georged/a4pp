using Eris.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eris.Api.Data;

public class ErisDbContext(DbContextOptions<ErisDbContext> options) : DbContext(options)
{
    public DbSet<RegistrationEndpoint> Registrations => Set<RegistrationEndpoint>();
    public DbSet<EventDeliveryRecord> EventRecords => Set<EventDeliveryRecord>();
    public DbSet<SimulationRun> SimulationRuns => Set<SimulationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegistrationEndpoint>()
            .HasIndex(x => x.EndpointUrl);

        modelBuilder.Entity<RegistrationEndpoint>()
            .HasIndex(x => x.OwnerKey);

        modelBuilder.Entity<EventDeliveryRecord>()
            .HasIndex(x => new { x.RegistrationEndpointId, x.SentAtUtc });

        modelBuilder.Entity<SimulationRun>()
            .HasIndex(x => x.RegistrationEndpointId);
    }
}

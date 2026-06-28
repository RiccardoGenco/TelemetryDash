using Microsoft.EntityFrameworkCore;
using TelemetryDash.Infrastructure.Data.Entities;

namespace TelemetryDash.Infrastructure.Data;

public class TelemetryDbContext : DbContext
{
    private readonly string _connectionString;

    public TelemetryDbContext() : this("Data Source=telemetry.db") { }

    public TelemetryDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ReadingEntity> Readings => Set<ReadingEntity>();
    public DbSet<AlarmEntity> Alarms => Set<AlarmEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Map SessionId explicitly as the foreign key. Without this, EF Core's
        // convention looks for "SessionEntityId" and creates a separate shadow FK
        // (left NULL by our writes), which breaks Include(s => s.Readings/Alarms).
        model.Entity<SessionEntity>(e =>
        {
            e.HasMany(s => s.Readings)
                .WithOne()
                .HasForeignKey(r => r.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.Alarms)
                .WithOne()
                .HasForeignKey(a => a.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ReadingEntity>(e =>
        {
            e.HasIndex(r => r.Timestamp);
            e.HasIndex(r => r.ChannelId);
            e.HasIndex(r => new { r.SessionId, r.Timestamp });
        });

        model.Entity<AlarmEntity>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.SessionId);
        });
    }
}

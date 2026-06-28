using Microsoft.EntityFrameworkCore;
using TelemetryDash.Infrastructure.Data.Entities;

namespace TelemetryDash.Infrastructure.Data;

public class TelemetryDbContext : DbContext
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<ReadingEntity> Readings => Set<ReadingEntity>();
    public DbSet<AlarmEntity> Alarms => Set<AlarmEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=telemetry.db");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
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

using Microsoft.EntityFrameworkCore;
using TelemetryDash.Infrastructure.Data;
using TelemetryDash.Infrastructure.Data.Entities;

namespace TelemetryDash.Tests;

public class TelemetryDbContextTests
{
    [Fact]
    public void GetSession_WithInclude_ReturnsReadingsAndAlarmsLinkedBySessionId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"td_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Pooling=False";
        var sessionId = Guid.NewGuid();

        try
        {
            using (var db = new TelemetryDbContext(connectionString))
            {
                db.Database.EnsureCreated();

                db.Sessions.Add(new SessionEntity
                {
                    Id = sessionId,
                    StartTime = DateTime.UtcNow,
                    DataSourceName = "Test",
                });
                db.Readings.Add(new ReadingEntity
                {
                    SessionId = sessionId,
                    ChannelId = "TEMP_A1",
                    Value = 42.0,
                    Timestamp = DateTime.UtcNow,
                    Quality = 0,
                });
                db.Alarms.Add(new AlarmEntity
                {
                    SessionId = sessionId,
                    ChannelId = "TEMP_A1",
                    Severity = 1,
                    Message = "test",
                    Timestamp = DateTime.UtcNow,
                    ReadingValue = 42.0,
                });
                db.SaveChanges();
            }

            // Fresh context, mirror what SessionRepository.GetSessionAsync does.
            using (var db = new TelemetryDbContext(connectionString))
            {
                var session = db.Sessions
                    .Include(s => s.Readings)
                    .Include(s => s.Alarms)
                    .FirstOrDefault(s => s.Id == sessionId);

                Assert.NotNull(session);
                Assert.Single(session!.Readings);
                Assert.Single(session.Alarms);
                Assert.Equal("TEMP_A1", session.Readings[0].ChannelId);
            }
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* temp file */ }
        }
    }
}

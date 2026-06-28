using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;
using TelemetryDash.Infrastructure.Data.Entities;

namespace TelemetryDash.Infrastructure.Data;

public class SessionRepository : ISessionRepository
{
    private readonly ILogger<SessionRepository> _logger;

    public SessionRepository(ILogger<SessionRepository> logger)
    {
        _logger = logger;
        using var db = new TelemetryDbContext();
        db.Database.EnsureCreated();
    }

    public async Task<Guid> CreateSessionAsync(TelemetrySession session)
    {
        using var db = new TelemetryDbContext();
        db.Sessions.Add(new SessionEntity
        {
            Id = session.Id,
            StartTime = session.StartTime,
            DataSourceName = session.DataSourceName,
        });
        await db.SaveChangesAsync();
        _logger.LogInformation("Session {Id} created", session.Id);
        return session.Id;
    }

    public async Task SaveReadingsAsync(Guid sessionId, IEnumerable<TelemetryReading> readings)
    {
        using var db = new TelemetryDbContext();
        var entities = readings.Select(r => new ReadingEntity
        {
            SessionId = sessionId,
            ChannelId = r.ChannelId,
            Value = r.Value,
            Timestamp = r.Timestamp,
            Quality = (byte)r.Quality,
        });
        db.Readings.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public async Task SaveAlarmAsync(Guid sessionId, AlarmResult alarm)
    {
        using var db = new TelemetryDbContext();
        db.Alarms.Add(new AlarmEntity
        {
            SessionId = sessionId,
            ChannelId = alarm.Reading.ChannelId,
            Severity = (int)alarm.Severity,
            Message = alarm.Message,
            Timestamp = alarm.Timestamp,
            ReadingValue = alarm.Reading.Value,
        });
        await db.SaveChangesAsync();
    }

    public async Task EndSessionAsync(Guid sessionId, DateTime endTime)
    {
        using var db = new TelemetryDbContext();
        var session = await db.Sessions.FindAsync(sessionId);
        if (session is not null)
        {
            session.EndTime = endTime;
            await db.SaveChangesAsync();
            _logger.LogInformation("Session {Id} ended", sessionId);
        }
    }

    public async Task<TelemetrySession?> GetSessionAsync(Guid sessionId)
    {
        using var db = new TelemetryDbContext();
        var entity = await db.Sessions
            .Include(s => s.Readings)
            .Include(s => s.Alarms)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (entity is null) return null;

        return MapToModel(entity);
    }

    public async Task<IReadOnlyList<TelemetrySession>> GetAllSessionsAsync()
    {
        using var db = new TelemetryDbContext();
        var entities = await db.Sessions
            .OrderByDescending(s => s.StartTime)
            .ToListAsync();

        return entities.Select(e => new TelemetrySession
        {
            Id = e.Id,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            DataSourceName = e.DataSourceName,
        }).ToList().AsReadOnly();
    }

    private static TelemetrySession MapToModel(SessionEntity entity)
    {
        return new TelemetrySession
        {
            Id = entity.Id,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            DataSourceName = entity.DataSourceName,
            Readings = entity.Readings.Select(r => new TelemetryReading
            {
                ChannelId = r.ChannelId,
                Value = r.Value,
                Timestamp = r.Timestamp,
                Quality = (QualityFlag)r.Quality,
            }).ToList(),
            Alarms = entity.Alarms.Select(a => new AlarmResult
            {
                IsAlarm = true,
                Severity = (AlarmSeverity)a.Severity,
                Timestamp = a.Timestamp,
                Message = a.Message,
                Reading = new TelemetryReading { ChannelId = a.ChannelId, Value = a.ReadingValue },
            }).ToList(),
        };
    }
}

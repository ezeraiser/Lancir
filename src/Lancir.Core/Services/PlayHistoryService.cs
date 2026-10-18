using Lancir.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancir.Core.Services;

/// <summary>
/// Read-only queries over recorded play sessions. Uses its own database scope per call so it can
/// run alongside the library view model's long-lived context.
/// </summary>
public sealed class PlayHistoryService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PlayHistoryService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Minutes played on each of the last <paramref name="days"/> local days (oldest first, today
    /// last) plus the most recent sessions.
    /// </summary>
    public async Task<PlayActivity> GetActivityAsync(int gameId, int days, int recentCount, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var today = DateTime.Now.Date;
        var since = today.AddDays(-(days - 1)).ToUniversalTime();

        var inRange = await db.PlaySessions
            .AsNoTracking()
            .Where(s => s.GameId == gameId && s.StartedAtUtc >= since)
            .ToListAsync(cancellationToken);

        var minutes = new double[days];
        foreach (var session in inRange)
        {
            var offset = (today - session.StartedAtUtc.ToLocalTime().Date).Days;
            if (offset >= 0 && offset < days)
            {
                minutes[days - 1 - offset] += session.DurationSeconds / 60.0;
            }
        }

        var recent = await db.PlaySessions
            .AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(recentCount)
            .ToListAsync(cancellationToken);

        return new PlayActivity(minutes, recent);
    }
}

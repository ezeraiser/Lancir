using Lancir.Core.Models;

namespace Lancir.Core.Services;

/// <summary>Play history of one game: minutes per day (oldest first) and the latest sessions.</summary>
public sealed record PlayActivity(IReadOnlyList<double> DailyMinutes, IReadOnlyList<PlaySession> RecentSessions);

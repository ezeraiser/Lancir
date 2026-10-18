namespace Lancir.Core.Models;

/// <summary>One continuous run of a library game's executable, recorded by PlaytimeTracker.</summary>
public sealed class PlaySession
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public long DurationSeconds { get; set; }
}

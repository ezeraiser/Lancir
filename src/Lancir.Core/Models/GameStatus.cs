namespace Lancir.Core.Models;

/// <summary>The user's own progress marker for a library game.</summary>
public enum GameStatus
{
    Backlog = 0,
    Playing = 1,
    Finished = 2,
    Dropped = 3,
}

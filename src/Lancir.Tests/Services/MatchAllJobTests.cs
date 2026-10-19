using Lancir.Core.Services;

namespace Lancir.Tests.Services;

public sealed class MatchAllJobTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"match-job-{Guid.NewGuid():N}.json");
    private DateTime _now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeMatcher(Func<int, CancellationToken, Task<bool>> match) : IGameMatcher
    {
        public List<int> Called { get; } = [];

        public async Task<bool> MatchAsync(int gameId, CancellationToken cancellationToken)
        {
            lock (Called)
            {
                Called.Add(gameId);
            }

            return await match(gameId, cancellationToken);
        }
    }

    private MatchAllJob JobWith(FakeMatcher matcher) => new(matcher, null, _file, () => _now);

    private static List<MatchJobGame> Games(params int[] ids) => ids.Select(i => new MatchJobGame(i, $"Game {i}")).ToList();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "timed out");
    }

    [Fact]
    public async Task MatchesTheGamesOneByOneAndCountsTheOutcomes()
    {
        var matcher = new FakeMatcher((id, _) => id switch
        {
            2 => throw new InvalidOperationException("service down"),
            3 => Task.FromResult(false),
            _ => Task.FromResult(true),
        });
        var job = JobWith(matcher);

        Assert.True(job.TryStart(Games(1, 2, 3, 4), skipRecent: false));
        await WaitUntilAsync(() => !job.IsRunning);

        var s = job.Snapshot;
        Assert.Equal(MatchJobStatus.Completed, s.Status);
        Assert.Equal((4, 4, 2, 1, 1, 0), (s.Total, s.Processed, s.Matched, s.NotFound, s.Errors, s.Skipped));
        Assert.Equal([1, 2, 3, 4], matcher.Called); // in order; the failing game did not stop the others
    }

    [Fact]
    public async Task OnlyOneJobRunsAtATimeUntilItIsCancelled()
    {
        var release = new TaskCompletionSource();
        var matcher = new FakeMatcher(async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return true;
        });
        var job = JobWith(matcher);

        Assert.True(job.TryStart(Games(1, 2, 3), skipRecent: false));
        await WaitUntilAsync(() => matcher.Called.Count == 1);
        Assert.False(job.TryStart(Games(1, 2, 3), skipRecent: false)); // refused: one is running

        await job.CancelAsync();

        Assert.Equal(MatchJobStatus.Cancelled, job.Snapshot.Status);
        Assert.True(job.TryStart(Games(1), skipRecent: false));      // now a new one may start
        release.SetResult();
        await WaitUntilAsync(() => !job.IsRunning);
    }

    [Fact]
    public async Task CancelledGameIsNotCountedAsTriedSoItIsMatchedLater()
    {
        var release = new TaskCompletionSource();
        var matcher = new FakeMatcher(async (id, ct) =>
        {
            if (id == 2)
            {
                await release.Task.WaitAsync(ct); // game 2 is being looked up when the job is cancelled
            }

            return true;
        });
        var job = JobWith(matcher);
        job.TryStart(Games(1, 2, 3), skipRecent: false);
        await WaitUntilAsync(() => matcher.Called.Contains(2));

        await job.CancelAsync();

        var s = job.Snapshot;
        Assert.Equal((MatchJobStatus.Cancelled, 1), (s.Status, s.Processed));
        Assert.Equal(1, job.CountRecentlyTried([1, 2, 3]));       // only game 1 was really done
    }

    [Fact]
    public async Task ContinuingSkipsWhatWasTriedWithinTheLastHourAndStartingOverDoesNot()
    {
        var matcher = new FakeMatcher((_, _) => Task.FromResult(false));
        var job = JobWith(matcher);
        job.TryStart(Games(1, 2, 3), skipRecent: false);
        await WaitUntilAsync(() => !job.IsRunning);

        _now += TimeSpan.FromMinutes(30);
        Assert.Equal(3, job.CountRecentlyTried([1, 2, 3, 4]));
        matcher.Called.Clear();
        job.TryStart(Games(1, 2, 3, 4), skipRecent: true);        // continue: only the new game 4 is left
        await WaitUntilAsync(() => !job.IsRunning);
        Assert.Equal([4], matcher.Called);
        Assert.Equal(3, job.Snapshot.Skipped);

        matcher.Called.Clear();
        job.TryStart(Games(1, 2, 3, 4), skipRecent: false);       // start over: everything again
        await WaitUntilAsync(() => !job.IsRunning);
        Assert.Equal([1, 2, 3, 4], matcher.Called);
    }

    [Fact]
    public async Task GamesTriedMoreThanAnHourAgoAreMatchedAgain()
    {
        var matcher = new FakeMatcher((_, _) => Task.FromResult(false));
        var job = JobWith(matcher);
        job.TryStart(Games(1, 2), skipRecent: false);
        await WaitUntilAsync(() => !job.IsRunning);

        _now += TimeSpan.FromMinutes(61);
        Assert.Equal(0, job.CountRecentlyTried([1, 2]));
        matcher.Called.Clear();
        job.TryStart(Games(1, 2), skipRecent: true);
        await WaitUntilAsync(() => !job.IsRunning);

        Assert.Equal([1, 2], matcher.Called);
    }

    [Fact]
    public async Task AJobRunningWhenTheAppClosedShowsAsInterruptedAndItsAttemptsAreKept()
    {
        var release = new TaskCompletionSource();
        var matcher = new FakeMatcher(async (id, ct) =>
        {
            if (id == 2)
            {
                await release.Task.WaitAsync(ct);
            }

            return true;
        });
        var job = JobWith(matcher);
        job.TryStart(Games(1, 2, 3), skipRecent: false);
        await WaitUntilAsync(() => matcher.Called.Contains(2));

        // "the app is closed" here: a new instance reads what the first one left on disk (it was saved as running)
        var afterRestart = JobWith(new FakeMatcher((_, _) => Task.FromResult(true)));

        Assert.Equal(MatchJobStatus.Interrupted, afterRestart.Snapshot.Status);
        Assert.Equal(1, afterRestart.CountRecentlyTried([1, 2, 3]));
        Assert.True(afterRestart.TryStart(Games(1, 2, 3), skipRecent: true)); // an interrupted job does not block a new one
        await WaitUntilAsync(() => !afterRestart.IsRunning);
        release.SetResult();
        await job.CancelAsync();
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
        }
    }
}

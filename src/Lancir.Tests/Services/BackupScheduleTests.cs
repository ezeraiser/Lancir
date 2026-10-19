using Lancir.Core.Services;

namespace Lancir.Tests.Services;

public class BackupScheduleTests
{
    private static DateTime Local(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Local);

    [Fact]
    public void DailyBackupIsDueOnceAfterEachMidnight()
    {
        var now = Local(2026, 10, 19, 9, 30); // a Monday morning
        var tookTodayAt0001 = Local(2026, 10, 19, 0, 1).ToUniversalTime();
        var tookYesterdayEvening = Local(2026, 10, 18, 21, 0).ToUniversalTime();

        Assert.False(BackupService.IsDue("daily", tookTodayAt0001, now));
        Assert.True(BackupService.IsDue("daily", tookYesterdayEvening, now));
        Assert.True(BackupService.IsDue("daily", null, now));
    }

    [Fact]
    public void MissedMidnightIsMadeUpOnTheNextStartAndOnlyOnce()
    {
        var lastNight = Local(2026, 10, 15, 0, 2).ToUniversalTime();      // taken on the 15th, app closed since
        var firstStart = Local(2026, 10, 19, 8, 0);
        Assert.True(BackupService.IsDue("daily", lastNight, firstStart));

        var madeUp = firstStart.AddMinutes(2).ToUniversalTime();          // taken two minutes after that start
        Assert.False(BackupService.IsDue("daily", madeUp, firstStart.AddHours(5)));
        Assert.True(BackupService.IsDue("daily", madeUp, Local(2026, 10, 20, 0, 0)));  // and due again at the next midnight
    }

    [Fact]
    public void WeeklyBackupFollowsMondayAndMonthlyTheFirst()
    {
        var wednesday = Local(2026, 10, 21, 12, 0);
        Assert.Equal(Local(2026, 10, 19), BackupService.LatestSlot("weekly", wednesday));
        Assert.Equal(Local(2026, 10, 1), BackupService.LatestSlot("monthly", wednesday));
        Assert.Equal(Local(2026, 10, 21), BackupService.LatestSlot("daily", wednesday));

        var sunday = Local(2026, 10, 25, 23, 59);
        Assert.Equal(Local(2026, 10, 19), BackupService.LatestSlot("weekly", sunday));

        var tookSunday = Local(2026, 10, 18, 12, 0).ToUniversalTime();     // the week before
        Assert.True(BackupService.IsDue("weekly", tookSunday, wednesday));
        Assert.False(BackupService.IsDue("weekly", Local(2026, 10, 19, 0, 5).ToUniversalTime(), sunday));
    }

    [Fact]
    public void NextSlotIsTheComingMidnight()
    {
        Assert.Equal(Local(2026, 10, 20), BackupService.NextSlot(Local(2026, 10, 19, 23, 59)));
        Assert.Equal(Local(2026, 10, 20), BackupService.NextSlot(Local(2026, 10, 19, 0, 0)));
        Assert.Equal(Local(2026, 11, 1), BackupService.NextSlot(Local(2026, 10, 31, 12, 0)));
    }
}

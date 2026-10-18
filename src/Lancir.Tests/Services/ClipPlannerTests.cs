using Lancir.Core.Services;

namespace Lancir.Tests.Services;

public class ClipPlannerTests
{
    [Fact]
    public void Plan_HeavyWideClip_IsScaledDownAndGivenThePresetBitrate()
    {
        // a 1170x658 clip at 8.5 Mbps (a real one from the cache)
        var target = ClipPlanner.Plan(1170, 658, 8_530_000, ClipQualityPreset.Balanced);

        Assert.NotNull(target);
        Assert.Equal(720, target.Width);
        Assert.Equal(404, target.Height); // 658 * 720 / 1170 = 404.9, rounded to an even number
        Assert.InRange(target.Bitrate, 1_480_000, 1_500_000);
    }

    [Theory]
    [InlineData(ClipQualityPreset.High, 960, 3_000_000)]
    [InlineData(ClipQualityPreset.Balanced, 720, 1_500_000)]
    [InlineData(ClipQualityPreset.Smallest, 640, 1_000_000)]
    public void Plan_PresetsSetWidthAndBitrateCaps(ClipQualityPreset preset, int width, int bitrate)
    {
        var target = ClipPlanner.Plan(1920, 1080, 20_000_000, preset)!;

        Assert.Equal(width, target.Width);
        Assert.InRange(target.Bitrate, bitrate * 0.98, bitrate); // 16:9 gets (almost exactly) the cap
    }

    [Fact]
    public void Plan_NeverEnlargesAPicture()
    {
        var target = ClipPlanner.Plan(600, 338, 4_000_000, ClipQualityPreset.High)!;

        Assert.Equal(600, target.Width);
        Assert.Equal(338, target.Height);
    }

    [Fact]
    public void Plan_OtherShapesGetABitrateByTheirArea()
    {
        // a wide, short clip (1170x374) has about 55% of the area of a 16:9 frame of the same width
        var wide = ClipPlanner.Plan(1170, 374, 4_000_000, ClipQualityPreset.Balanced)!;
        var normal = ClipPlanner.Plan(1170, 658, 4_000_000, ClipQualityPreset.Balanced)!;

        Assert.True(wide.Bitrate < normal.Bitrate);
        Assert.InRange(wide.Bitrate, 700_000, 900_000);
    }

    [Fact]
    public void Plan_ClipsThatAreSmallAlready_AreLeftAlone()
    {
        // 600x338 at 400 kbps is already below what the preset would produce
        Assert.Null(ClipPlanner.Plan(600, 338, 400_000, ClipQualityPreset.Balanced));
        // just a little above the target is not worth a quality loss either
        Assert.Null(ClipPlanner.Plan(720, 405, 1_700_000, ClipQualityPreset.Balanced));
    }

    [Fact]
    public void Plan_DimensionsAreEvenAndBitrateHasAFloor()
    {
        var target = ClipPlanner.Plan(1001, 577, 9_000_000, ClipQualityPreset.Smallest)!;

        Assert.Equal(0, target.Width % 2);
        Assert.Equal(0, target.Height % 2);
        Assert.True(ClipPlanner.Plan(1920, 40, 9_000_000, ClipQualityPreset.Smallest)!.Bitrate >= 250_000);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 100, 100, 50)]
    public void Plan_NonsenseInputIsRejected(int width, int height, long bitrate, int unused)
    {
        _ = unused;
        Assert.Null(ClipPlanner.Plan(width, height, bitrate, ClipQualityPreset.Balanced));
    }

    [Theory]
    [InlineData(1000, 700, true)]
    [InlineData(1000, 750, true)]
    [InlineData(1000, 760, false)]
    [InlineData(1000, 1200, false)]
    [InlineData(0, 100, false)]
    [InlineData(1000, 0, false)]
    public void IsWorthKeeping_NeedsAtLeastAQuarterSaving(long original, long result, bool expected) =>
        Assert.Equal(expected, ClipPlanner.IsWorthKeeping(original, result));
}

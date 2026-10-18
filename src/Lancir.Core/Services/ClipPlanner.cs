namespace Lancir.Core.Services;

/// <summary>How hard the store-description clips are re-encoded: a bigger picture and bit rate keeps more detail.</summary>
public enum ClipQualityPreset
{
    /// <summary>Up to 960 px wide at up to 3 Mbps: closest to the original.</summary>
    High,

    /// <summary>Up to 720 px wide at up to 1.5 Mbps (default).</summary>
    Balanced,

    /// <summary>Up to 640 px wide at up to 1 Mbps: smallest files.</summary>
    Smallest,
}

/// <summary>The size and bit rate a clip is re-encoded to.</summary>
public sealed record ClipTarget(int Width, int Height, int Bitrate);

/// <summary>
/// Decides, from a clip's size and bit rate, what it should be re-encoded to - or that it should be left alone. Pure logic,
/// kept apart from the Windows encoder so it can be tested. The clips are tiles of about 480 px that loop for a few seconds, so
/// they are scaled down to the preset's width (never up) and given a bit rate that scales with the picture area.
/// </summary>
public static class ClipPlanner
{
    /// <summary>A clip must get at least this much smaller (as a fraction) to be worth replacing.</summary>
    public const double MinimumSaving = 0.25;

    private static (int MaxWidth, int CapBitrate) Limits(ClipQualityPreset preset) => preset switch
    {
        ClipQualityPreset.High => (960, 3_000_000),
        ClipQualityPreset.Smallest => (640, 1_000_000),
        _ => (720, 1_500_000),
    };

    /// <summary>
    /// The target for a clip of <paramref name="width"/> x <paramref name="height"/> that currently runs at
    /// <paramref name="bitrate"/> bits per second, or null when re-encoding would not save enough (it is small already).
    /// </summary>
    public static ClipTarget? Plan(int width, int height, long bitrate, ClipQualityPreset preset)
    {
        if (width < 2 || height < 2 || bitrate <= 0)
        {
            return null;
        }

        var (maxWidth, cap) = Limits(preset);
        var scale = Math.Min(1.0, (double)maxWidth / width);
        var targetWidth = Even(width * scale);
        var targetHeight = Even(height * scale);

        // The cap is for a 16:9 picture at the preset's full width; other shapes get a share by their picture area.
        var referenceArea = maxWidth * (maxWidth * 9.0 / 16.0);
        var area = (double)targetWidth * targetHeight;
        var target = (int)Math.Clamp(cap * (area / referenceArea), 250_000, cap);

        // Already at or near the target bit rate and size: re-encoding only loses quality.
        var alreadySmall = bitrate * (1 - MinimumSaving) <= target && targetWidth >= width;
        return alreadySmall ? null : new ClipTarget(targetWidth, targetHeight, target);
    }

    /// <summary>True when the new file is small enough compared with the original to replace it.</summary>
    public static bool IsWorthKeeping(long originalBytes, long newBytes) =>
        originalBytes > 0 && newBytes > 0 && newBytes <= originalBytes * (1 - MinimumSaving);

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}

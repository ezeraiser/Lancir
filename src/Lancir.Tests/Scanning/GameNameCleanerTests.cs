using Lancir.Core.Scanning;

namespace Lancir.Tests.Scanning;

public class GameNameCleanerTests
{
    [Theory]
    [InlineData("Hollow.Knight.v1.5.78.11833-FitGirl.Repack", "Hollow Knight")]
    [InlineData("Stardew Valley [1.6.9] (GOG)", "Stardew Valley")]
    [InlineData("Half-Life 2 2004 Complete Edition", "Half Life 2")]
    [InlineData("Portal2-RELOADED", "Portal2")]
    [InlineData("The Witcher 3 Wild Hunt GOTY Edition", "The Witcher 3 Wild Hunt")]
    public void Clean_StripsReleaseTagsYearsAndBrackets(string raw, string expected)
    {
        var result = GameNameCleaner.Clean(raw);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Clean_CollapsesWhitespaceAndSeparators()
    {
        var result = GameNameCleaner.Clean("My___Cool.Game--Title");

        Assert.Equal("My Cool Game Title", result);
    }

    [Fact]
    public void Clean_ThrowsOnEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => GameNameCleaner.Clean(" "));
    }
}

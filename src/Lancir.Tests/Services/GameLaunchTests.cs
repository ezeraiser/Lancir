using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.Tests.Services;

public class GameLaunchTests
{
    private static LibraryGame Game(Action<LibraryGame>? configure = null)
    {
        var game = new LibraryGame { FolderPath = "", DisplayName = "Test" };
        configure?.Invoke(game);
        return game;
    }

    [Theory]
    [InlineData("steam://rungameid/10", true)]
    [InlineData("com.epicgames.launcher://apps/abc?action=launch", true)]
    [InlineData("goggalaxy://openGameView/123", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.Foo_8wekyb3d8bbwe!App", true)]
    [InlineData(@"C:\Games\game.exe", false)]
    [InlineData("file:///C:/x.exe", false)]
    [InlineData("game.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidLaunchUri_AcceptsProtocolsOnly(string? value, bool expected) =>
        Assert.Equal(expected, GameLaunchService.IsValidLaunchUri(value));

    [Fact]
    public void BuildLaunchUri_SteamWithoutArguments_UsesRunGameId() =>
        Assert.Equal("steam://rungameid/230410",
            GameLaunchService.BuildLaunchUri(Game(g => { g.LaunchViaSteam = true; g.SteamAppId = 230410; })));

    [Fact]
    public void BuildLaunchUri_SteamWithArguments_PassesThemEncoded() =>
        Assert.Equal("steam://run/230410//-windowed%20-nointro/",
            GameLaunchService.BuildLaunchUri(Game(g =>
            {
                g.LaunchViaSteam = true;
                g.SteamAppId = 230410;
                g.LaunchArguments = "-windowed -nointro";
            })));

    [Fact]
    public void BuildLaunchUri_CustomUriWinsOverSteam() =>
        Assert.Equal("goggalaxy://openGameView/1",
            GameLaunchService.BuildLaunchUri(Game(g =>
            {
                g.LaunchViaSteam = true;
                g.SteamAppId = 10;
                g.LaunchUri = " goggalaxy://openGameView/1 ";
            })));

    [Fact]
    public void BuildLaunchUri_SteamNeedsAnId() =>
        Assert.Null(GameLaunchService.BuildLaunchUri(Game(g => g.LaunchViaSteam = true)));

    [Fact]
    public void BuildLaunchUri_InvalidCustomUriIsIgnored() =>
        Assert.Null(GameLaunchService.BuildLaunchUri(Game(g => g.LaunchUri = @"C:\x.exe")));

    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(true, "", "  ", true)]
    [InlineData(true, @"C:\g.exe", null, false)]
    [InlineData(true, null, "Game", false)]
    [InlineData(false, null, null, false)]
    public void LacksPlaytimeTracking_OnlyWhenStartedByAddressWithNothingToWatch(
        bool byAddress, string? exe, string? tracked, bool expected) =>
        Assert.Equal(expected, GameLaunchService.LacksPlaytimeTracking(byAddress, exe, tracked));

    [Fact]
    public void ParseProcessNames_SplitsStripsExeAndDeduplicates() =>
        Assert.Equal(["Game-Win64-Shipping", "helper", "Other"],
            PlaytimeTracker.ParseProcessNames("Game-Win64-Shipping.exe, helper;Helper.EXE\nOther"));

    [Fact]
    public void ParseProcessNames_EmptyGivesNothing()
    {
        Assert.Empty(PlaytimeTracker.ParseProcessNames(null));
        Assert.Empty(PlaytimeTracker.ParseProcessNames(" , ;"));
        Assert.Empty(PlaytimeTracker.ParseProcessNames(".exe"));
    }

    [Fact]
    public void SteamLocator_ParsesInstallDirAndLibraryPaths()
    {
        const string manifest = "\"AppState\"\n{\n\t\"appid\"\t\t\"230410\"\n\t\"installdir\"\t\t\"Warframe\"\n}";
        Assert.Equal("Warframe", SteamLocator.ParseInstallDir(manifest));
        Assert.Null(SteamLocator.ParseInstallDir("\"AppState\" { }"));

        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n" +
                           "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLocator.ParseLibraryPaths(vdf));
    }
}

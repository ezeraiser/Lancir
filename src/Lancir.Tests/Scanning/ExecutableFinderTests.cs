using Lancir.Core.Scanning;

namespace Lancir.Tests.Scanning;

public class ExecutableFinderTests : IDisposable
{
    private static readonly HashSet<string> NoIgnored = [];

    private readonly string _tempFolder;

    public ExecutableFinderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "LancirTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    [Fact]
    public void FindCandidates_OrdersRootBeforeNestedAndSkipsInstallers()
    {
        File.WriteAllBytes(Path.Combine(_tempFolder, "unins000.exe"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_tempFolder, "Game.exe"), new byte[1000]);

        var nestedDir = Path.Combine(_tempFolder, "bin");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllBytes(Path.Combine(nestedDir, "GameEngine.exe"), new byte[5000]);

        var result = ExecutableFinder.FindCandidates(_tempFolder, NoIgnored);

        Assert.Equal(2, result.Count);
        Assert.Equal(Path.Combine(_tempFolder, "Game.exe"), result[0]);
        Assert.Equal(Path.Combine(nestedDir, "GameEngine.exe"), result[1]);
    }

    [Fact]
    public void FindCandidates_ExcludesIgnoredPaths()
    {
        var gamePath = Path.Combine(_tempFolder, "Game.exe");
        File.WriteAllBytes(gamePath, new byte[1000]);
        File.WriteAllBytes(Path.Combine(_tempFolder, "Other.exe"), new byte[500]);

        var result = ExecutableFinder.FindCandidates(_tempFolder, new HashSet<string> { gamePath });

        Assert.Single(result);
        Assert.DoesNotContain(gamePath, result);
    }

    [Fact]
    public void FindCandidates_NoExecutables_ReturnsEmpty()
    {
        var result = ExecutableFinder.FindCandidates(_tempFolder, NoIgnored);

        Assert.Empty(result);
    }

    [Fact]
    public void FindCandidates_NonexistentFolder_ReturnsEmpty()
    {
        var result = ExecutableFinder.FindCandidates(Path.Combine(_tempFolder, "does-not-exist"), NoIgnored);

        Assert.Empty(result);
    }
}

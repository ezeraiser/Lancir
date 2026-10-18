using Lancir.Core.Scanning;

namespace Lancir.Tests.Scanning;

public class FolderScannerTests : IDisposable
{
    private readonly string _tempFolder;

    public FolderScannerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "LancirScanTests_" + Guid.NewGuid());
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
    public void Scan_WatchedFolderIsItselfAGame_ReturnsRootAsCandidate()
    {
        // The watched root itself contains an exe directly (user pointed at a single game folder).
        File.WriteAllBytes(Path.Combine(_tempFolder, "Game.exe"), new byte[1000]);

        var result = FolderScanner.Scan([_tempFolder]);

        Assert.Single(result);
        Assert.Equal(_tempFolder, result[0].FolderPath);
    }

    [Fact]
    public void Scan_WatchedFolderIsACollection_ReturnsEachSubfolderAsCandidate()
    {
        var gameA = Path.Combine(_tempFolder, "GameA");
        var gameB = Path.Combine(_tempFolder, "GameB");
        Directory.CreateDirectory(gameA);
        Directory.CreateDirectory(gameB);
        File.WriteAllBytes(Path.Combine(gameA, "A.exe"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(gameB, "B.exe"), new byte[1000]);

        var result = FolderScanner.Scan([_tempFolder]);

        // The watched root itself is also a candidate (it recursively finds both exes too) —
        // this overlap is expected/accepted: the user picks whichever candidate they want.
        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.FolderPath == _tempFolder);
        Assert.Contains(result, e => e.FolderPath == gameA);
        Assert.Contains(result, e => e.FolderPath == gameB);
    }

    [Fact]
    public void Scan_SubfolderWithNoExecutable_IsNotReturned()
    {
        var emptyGame = Path.Combine(_tempFolder, "NotAGame");
        Directory.CreateDirectory(emptyGame);
        File.WriteAllText(Path.Combine(emptyGame, "readme.txt"), "hi");

        var result = FolderScanner.Scan([_tempFolder]);

        Assert.Empty(result);
    }

    [Fact]
    public void Scan_IgnoredFolder_IsExcludedAsItsOwnCandidate()
    {
        var game = Path.Combine(_tempFolder, "GameA");
        Directory.CreateDirectory(game);
        File.WriteAllBytes(Path.Combine(game, "A.exe"), new byte[1000]);

        var result = FolderScanner.Scan([_tempFolder], ignoredFolderPaths: new HashSet<string> { game });

        Assert.DoesNotContain(result, e => e.FolderPath == game);
    }
}

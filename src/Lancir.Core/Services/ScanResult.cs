using Lancir.Core.Models;

namespace Lancir.Core.Services;

/// <summary>
/// A single scanned game folder, ready for user review. Scanning is offline-only (folder name +
/// executable candidates); matching happens later, on demand, via
/// <see cref="LibraryService.MatchGameAsync"/>.
/// </summary>
public sealed class ScanResult
{
    public required GameEntry Entry { get; init; }
}

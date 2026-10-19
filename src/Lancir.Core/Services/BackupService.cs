using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lancir.Core.Services;

/// <summary>What a backup says about itself (the first entry of its zip).</summary>
public sealed record BackupManifest(string AppVersion, DateTime CreatedUtc, bool IncludesMedia, string Kind);

/// <summary>A backup file in the backup folder.</summary>
public sealed record BackupFileInfo(string Path, string Name, string Kind, DateTime CreatedUtc, long SizeBytes);

/// <summary>
/// Backs up everything of the app into one zip: the library database (a consistent copy, even while the app runs), the settings
/// (the Steam key inside stays encrypted for this Windows account), the user's library icons and, when asked, the pictures
/// (covers, backgrounds, store pictures, kept originals, achievement icons). Thumbnails, trailers and the browser data are left out:
/// they are rebuilt on their own. A backup is restored by unpacking it next to the data and swapping the files in at the next start
/// (the database cannot be replaced while it is open): see <see cref="StageRestore"/> and <see cref="ApplyPendingRestore"/>.
/// </summary>
public sealed class BackupService(SettingsService settingsService, DebugLogService log)
{
    public const string ManifestName = "manifest.json";
    private const string FilePrefix = "Lancir-yedek-";

    // cache subfolders that belong in a backup with pictures (the rest of cache/ is rebuilt by the app)
    private static readonly string[] MediaFolders = ["images", "steam", "originals", "achievements", "library-icons"];

    private static readonly HashSet<string> AlreadyCompressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".gif", ".mp4", ".webm", ".zip",
    };

    private static string StagingFolder => Path.Combine(AppPaths.AppDataFolder, "restore-staging");

    /// <summary>The folder backups are written to: the one chosen in Settings, or "backups" in the data folder.</summary>
    public string BackupFolder => settingsService.Load().BackupFolder is { Length: > 0 } custom
        ? custom
        : Path.Combine(AppPaths.AppDataFolder, "backups");

    // ---------------------------------------------------------------- creating

    /// <summary>Writes a backup zip and returns its path. <paramref name="kind"/> is "daily", "weekly", "monthly" or "manual".</summary>
    public Task<string> CreateAsync(string kind, bool includeMedia) => Task.Run(() => Create(kind, includeMedia));

    private string Create(string kind, bool includeMedia)
    {
        var folder = BackupFolder;
        Directory.CreateDirectory(folder);
        var snapshot = Path.Combine(folder, ".library-snapshot.db");
        var name = $"{FilePrefix}{kind}-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
        var finalPath = Path.Combine(folder, name);
        var partPath = finalPath + ".part";

        try
        {
            SnapshotDatabase(snapshot);
            File.Delete(partPath);
            using (var zip = ZipFile.Open(partPath, ZipArchiveMode.Create))
            {
                var manifest = new BackupManifest(
                    typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1", DateTime.UtcNow, includeMedia, kind);
                var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using (var writer = entry.Open())
                {
                    JsonSerializer.Serialize(writer, manifest);
                }

                AddFile(zip, snapshot, "library.db", CompressionLevel.Optimal);
                AddFile(zip, AppPaths.SettingsPath, "settings.json", CompressionLevel.Optimal);

                var cache = Path.Combine(AppPaths.AppDataFolder, "cache");
                if (Directory.Exists(cache))
                {
                    // the small history files that sit loose in cache/ (what was optimized already, and so on)
                    foreach (var file in Directory.EnumerateFiles(cache, "*.*").Where(f => f.EndsWith(".json") || f.EndsWith(".txt")))
                    {
                        AddFile(zip, file, "cache/" + Path.GetFileName(file), CompressionLevel.Optimal);
                    }

                    if (includeMedia)
                    {
                        foreach (var sub in MediaFolders)
                        {
                            var source = Path.Combine(cache, sub);
                            if (!Directory.Exists(source))
                            {
                                continue;
                            }

                            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                            {
                                var entryName = "cache/" + Path.GetRelativePath(cache, file).Replace('\\', '/');
                                var level = AlreadyCompressed.Contains(Path.GetExtension(file)) ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
                                AddFile(zip, file, entryName, level);
                            }
                        }
                    }
                }
            }

            File.Move(partPath, finalPath, overwrite: true);
            log.Log($"Yedek alındı: {name} ({new FileInfo(finalPath).Length / 1048576} MB).");
            return finalPath;
        }
        catch
        {
            try
            {
                File.Delete(partPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
        finally
        {
            try
            {
                File.Delete(snapshot);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A consistent copy of the live database (SQLite's own backup, safe while the app writes to it).</summary>
    private static void SnapshotDatabase(string target)
    {
        File.Delete(target);
        if (!File.Exists(AppPaths.DatabasePath))
        {
            return;
        }

        using var source = new SqliteConnection($"Data Source={AppPaths.DatabasePath};Mode=ReadOnly");
        source.Open();
        using var destination = new SqliteConnection($"Data Source={target};Pooling=False");
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void AddFile(ZipArchive zip, string path, string entryName, CompressionLevel level)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            // read with sharing: the app may be writing other files at the same time; a file that vanishes is simply skipped
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var entry = zip.CreateEntry(entryName, level);
            using var output = entry.Open();
            input.CopyTo(output, 64 * 1024);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not in the backup this time
        }
    }

    // ---------------------------------------------------------------- the schedule

    /// <summary>The time of day the automatic backups are scheduled for (local time): midnight.</summary>
    public static readonly TimeSpan ScheduledTime = TimeSpan.Zero;

    /// <summary>The most recent scheduled moment (local time) of a kind: last midnight; the Monday of this week; the 1st of this month.</summary>
    public static DateTime LatestSlot(string kind, DateTime nowLocal)
    {
        var today = nowLocal.Date + ScheduledTime;
        if (today > nowLocal)
        {
            today = today.AddDays(-1);
        }

        return kind switch
        {
            "daily" => today,
            "weekly" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            _ => new DateTime(today.Year, today.Month, 1) + ScheduledTime,
        };
    }

    /// <summary>The next scheduled moment (local time) after <paramref name="nowLocal"/>: the coming midnight.</summary>
    public static DateTime NextSlot(DateTime nowLocal)
    {
        var next = nowLocal.Date + ScheduledTime;
        return next <= nowLocal ? next.AddDays(1) : next;
    }

    /// <summary>
    /// A backup is due when none was taken since the kind's latest scheduled moment: one missed because the app was closed at that time
    /// is therefore taken as soon as the app runs again, and only once.
    /// </summary>
    public static bool IsDue(string kind, DateTime? lastUtc, DateTime nowLocal) =>
        lastUtc is null || lastUtc.Value.ToLocalTime() < LatestSlot(kind, nowLocal);

    /// <summary>The kinds (daily, weekly, monthly) that are switched on in Settings and due now.</summary>
    public List<string> DueKinds()
    {
        var settings = settingsService.Load();
        var now = DateTime.Now;
        var due = new List<string>();
        if (settings.BackupMonthlyEnabled && IsDue("monthly", settings.LastBackupMonthlyUtc, now))
        {
            due.Add("monthly");
        }

        if (settings.BackupWeeklyEnabled && IsDue("weekly", settings.LastBackupWeeklyUtc, now))
        {
            due.Add("weekly");
        }

        if (settings.BackupDailyEnabled && IsDue("daily", settings.LastBackupDailyUtc, now))
        {
            due.Add("daily");
        }

        return due;
    }

    /// <summary>
    /// Takes the backups that are due (see <see cref="IsDue"/>). One zip is written for all that are due; it is copied under the
    /// other names. Returns true when a backup was taken.
    /// </summary>
    public async Task<bool> RunDueBackupsAsync()
    {
        var settings = settingsService.Load();
        var now = DateTime.UtcNow;
        var due = DueKinds();
        if (due.Count == 0)
        {
            return false;
        }

        var first = await CreateAsync(due[0], settings.BackupIncludeMedia);
        foreach (var other in due.Skip(1))
        {
            File.Copy(first, first.Replace($"-{due[0]}-", $"-{other}-"), overwrite: true);
        }

        var saved = settingsService.Load();
        foreach (var kind in due)
        {
            switch (kind)
            {
                case "daily":
                    saved.LastBackupDailyUtc = now;
                    break;
                case "weekly":
                    saved.LastBackupWeeklyUtc = now;
                    break;
                default:
                    saved.LastBackupMonthlyUtc = now;
                    break;
            }

            Prune(kind, Math.Clamp(saved.BackupKeepCount, 1, 100));
        }

        settingsService.Save(saved);
        return true;
    }

    /// <summary>Keeps the newest <paramref name="keep"/> backups of a kind and deletes the older ones.</summary>
    public void Prune(string kind, int keep)
    {
        foreach (var old in List().Where(b => b.Kind == kind).OrderByDescending(b => b.CreatedUtc).Skip(keep))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---------------------------------------------------------------- listing

    public IReadOnlyList<BackupFileInfo> List()
    {
        var folder = BackupFolder;
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, FilePrefix + "*.zip")
            .Select(path => new FileInfo(path))
            .Select(info =>
            {
                var parts = Path.GetFileNameWithoutExtension(info.Name)[FilePrefix.Length..].Split('-', 2);
                return new BackupFileInfo(info.FullName, info.Name, parts[0], info.LastWriteTimeUtc, info.Length);
            })
            .OrderByDescending(b => b.CreatedUtc)
            .ToList();
    }

    // ---------------------------------------------------------------- restoring

    /// <summary>Reads the manifest of a backup zip; null when the file is not a Lancir backup.</summary>
    public static BackupManifest? ReadManifest(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestName);
            if (entry is null || zip.GetEntry("library.db") is null || zip.GetEntry("settings.json") is null)
            {
                return null;
            }

            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Unpacks a backup next to the data; it replaces the current data at the next start of the app (see <see cref="ApplyPendingRestore"/>).</summary>
    public static void StageRestore(string zipPath)
    {
        if (ReadManifest(zipPath) is null)
        {
            throw new InvalidDataException("not a Lancir backup");
        }

        var staging = StagingFolder;
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // a folder entry
            }

            var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("unsafe path in the backup");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>
    /// Swaps in a staged backup, before the database is opened. The files being replaced are first copied to backups/vor-restore-… so a
    /// restore can be undone. Returns an error message, or null when nothing was pending or all went well.
    /// </summary>
    public static string? ApplyPendingRestore()
    {
        var staging = StagingFolder;
        if (!Directory.Exists(staging))
        {
            return null;
        }

        try
        {
            if (!File.Exists(Path.Combine(staging, "library.db")) || !File.Exists(Path.Combine(staging, "settings.json")))
            {
                Directory.Delete(staging, recursive: true);
                return null;
            }

            var safety = Path.Combine(AppPaths.AppDataFolder, "backups", $"restore-oncesi-{DateTime.Now:yyyy-MM-dd_HHmmss}");
            Directory.CreateDirectory(safety);
            foreach (var file in new[] { AppPaths.DatabasePath, AppPaths.SettingsPath })
            {
                if (File.Exists(file))
                {
                    File.Copy(file, Path.Combine(safety, Path.GetFileName(file)), overwrite: true);
                }
            }

            foreach (var leftover in new[] { AppPaths.DatabasePath + "-wal", AppPaths.DatabasePath + "-shm" })
            {
                File.Delete(leftover);
            }

            File.Move(Path.Combine(staging, "library.db"), AppPaths.DatabasePath, overwrite: true);
            File.Move(Path.Combine(staging, "settings.json"), AppPaths.SettingsPath, overwrite: true);

            var stagedCache = Path.Combine(staging, "cache");
            var cache = Path.Combine(AppPaths.AppDataFolder, "cache");
            if (Directory.Exists(stagedCache))
            {
                Directory.CreateDirectory(cache);
                foreach (var file in Directory.EnumerateFiles(stagedCache))
                {
                    File.Move(file, Path.Combine(cache, Path.GetFileName(file)), overwrite: true);
                }

                foreach (var sub in MediaFolders)
                {
                    var stagedSub = Path.Combine(stagedCache, sub);
                    if (!Directory.Exists(stagedSub))
                    {
                        continue;
                    }

                    ReplaceFolder(stagedSub, Path.Combine(cache, sub));
                }
            }

            Directory.Delete(staging, recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Puts a staged folder in place of the current one. The current folder is only renamed first and deleted at the end, so a failure
    /// half way never leaves the app without its pictures; when a plain move is refused the files are copied instead.
    /// </summary>
    private static void ReplaceFolder(string staged, string target)
    {
        var old = target + ".replaced";
        if (Directory.Exists(old))
        {
            Directory.Delete(old, recursive: true);
        }

        if (Directory.Exists(target))
        {
            Directory.Move(target, old);
        }

        try
        {
            try
            {
                Directory.Move(staged, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CopyFolder(staged, target);
            }
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(old))
            {
                Directory.Move(old, target);
            }
            else if (Directory.Exists(old))
            {
                Directory.Delete(target, recursive: true);
                Directory.Move(old, target);
            }

            throw;
        }

        try
        {
            Directory.Delete(old, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an old copy left behind only takes room
        }
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}

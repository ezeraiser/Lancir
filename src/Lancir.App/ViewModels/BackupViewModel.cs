using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

/// <summary>A backup in the list of Settings.</summary>
public sealed record BackupItem(string Path, string Name, string KindText, string DateText, string SizeText);

/// <summary>The "Yedekleme" section of Settings: the automatic schedule, taking a backup now, and restoring one.</summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly BackupService _backups;
    private bool _loading;

    public BackupViewModel(SettingsService settings, BackupService backups)
    {
        _settings = settings;
        _backups = backups;
        Load();
        BackupScheduler.BackupDone += Refresh;
    }

    public ObservableCollection<BackupItem> Items { get; } = [];

    [ObservableProperty]
    private bool _dailyEnabled;

    [ObservableProperty]
    private bool _weeklyEnabled;

    [ObservableProperty]
    private bool _monthlyEnabled;

    [ObservableProperty]
    private bool _includeMedia = true;

    /// <summary>The choices for how many backups of each kind are kept.</summary>
    public IReadOnlyList<IntOption> KeepOptions { get; } =
        [.. new[] { 1, 2, 3, 4, 5, 7, 10, 14, 20, 30 }.Select(n => new IntOption(n, n.ToString()))];

    /// <summary>How many automatic backups of each kind are kept (the older ones go when a new one is taken).</summary>
    [ObservableProperty]
    private int _keepCount = 3;

    [ObservableProperty]
    private string _folderText = "";

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupNowCommand))]
    private bool _isBusy;

    partial void OnDailyEnabledChanged(bool value) => Save();

    partial void OnWeeklyEnabledChanged(bool value) => Save();

    partial void OnMonthlyEnabledChanged(bool value) => Save();

    partial void OnIncludeMediaChanged(bool value) => Save();

    partial void OnKeepCountChanged(int value) => Save();

    private void Load()
    {
        var s = _settings.Load();
        _loading = true;
        DailyEnabled = s.BackupDailyEnabled;
        WeeklyEnabled = s.BackupWeeklyEnabled;
        MonthlyEnabled = s.BackupMonthlyEnabled;
        IncludeMedia = s.BackupIncludeMedia;
        KeepCount = KeepOptions.Any(o => o.Value == s.BackupKeepCount) ? s.BackupKeepCount : 3;
        _loading = false;
        FolderText = _backups.BackupFolder;
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        var s = _settings.Load();
        s.BackupDailyEnabled = DailyEnabled;
        s.BackupWeeklyEnabled = WeeklyEnabled;
        s.BackupMonthlyEnabled = MonthlyEnabled;
        s.BackupIncludeMedia = IncludeMedia;
        s.BackupKeepCount = KeepCount;
        _settings.Save(s);
        BackupScheduler.RequestCheck(); // a kind that was just switched on and is due is taken now

    }

    /// <summary>Reads the backup folder again (when the page opens, after a backup, after deleting one).</summary>
    public void Refresh()
    {
        FolderText = _backups.BackupFolder;
        Items.Clear();
        foreach (var backup in _backups.List())
        {
            Items.Add(new BackupItem(
                backup.Path, backup.Name,
                backup.Kind switch
                {
                    "daily" => Loc.T("Günlük"),
                    "weekly" => Loc.T("Haftalık"),
                    "monthly" => Loc.T("Aylık"),
                    _ => Loc.T("Elle"),
                },
                backup.CreatedUtc.ToLocalTime().ToString("g", AppLanguage.Culture),
                $"{backup.SizeBytes / 1048576.0:F1} MB"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanBackupNow))]
    private async Task BackupNowAsync()
    {
        IsBusy = true;
        Status = Loc.T("Yedek alınıyor…");
        try
        {
            var path = await _backups.CreateAsync("manual", IncludeMedia);
            Status = Loc.F("Yedek alındı: {0}", Path.GetFileName(path));
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Microsoft.Data.Sqlite.SqliteException)
        {
            Status = Loc.F("Yedek alınamadı: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanBackupNow() => !IsBusy;

    [RelayCommand]
    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Yedeklerin kaydedileceği klasör"), InitialDirectory = _backups.BackupFolder };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var s = _settings.Load();
        s.BackupFolder = dialog.FolderName;
        _settings.Save(s);
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = _backups.BackupFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>"Yükle": pick a backup zip anywhere and restore it.</summary>
    [RelayCommand]
    private async Task RestoreFromFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Geri yüklenecek yedeği seçin"),
            Filter = Loc.T("Lancir yedeği") + "|*.zip",
            InitialDirectory = Directory.Exists(_backups.BackupFolder) ? _backups.BackupFolder : null,
        };
        if (dialog.ShowDialog() == true)
        {
            await RestoreAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private async Task RestoreItemAsync(BackupItem? item)
    {
        if (item is not null)
        {
            await RestoreAsync(item.Path);
        }
    }

    [RelayCommand]
    private void DeleteItem(BackupItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (MessageBox.Show(Application.Current.MainWindow, Loc.F("\"{0}\" silinsin mi?", item.Name), Loc.T("Yedekleme"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Loc.F("Silinemedi: {0}", ex.Message);
        }

        Refresh();
    }

    private async Task RestoreAsync(string zipPath)
    {
        var manifest = BackupService.ReadManifest(zipPath);
        if (manifest is null)
        {
            MessageBox.Show(Application.Current.MainWindow, Loc.T("Bu dosya bir Lancir yedeği değil."), Loc.T("Yedekleme"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var message = Loc.F(
            "Bu yedek {0} tarihli ve {1}. Geri yüklenince kütüphaneniz, ayarlarınız ve (yedekte varsa) görselleriniz yedektekilerle değiştirilir; mevcut veriler \"backups\" klasörüne ayrıca kopyalanır. Değişikliklerin uygulanması için Lancir'in yeniden başlatılması gerekir. Devam edilsin mi?",
            manifest.CreatedUtc.ToLocalTime().ToString("g", AppLanguage.Culture),
            manifest.IncludesMedia ? Loc.T("görselleri de içeriyor") : Loc.T("görselleri içermiyor"));
        if (MessageBox.Show(Application.Current.MainWindow, message, Loc.T("Yedekten geri yükle"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            BackupService.StageRestore(zipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Status = Loc.F("Geri yüklenemedi: {0}", ex.Message);
            return;
        }

        // The files are ready: say so and restart, which swaps them in (the app cannot replace its own database while it runs).
        Status = Loc.T("Yedek başarıyla yüklendi. Lancir yeniden başlatılıyor…");
        await Task.Delay(2500); // long enough to be read before the window closes
        App.RestartForRestore();
    }
}

using System.IO;
using System.Windows;
using Lancir.App.Services;
using Lancir.App.ViewModels;
using Lancir.Core.Data;
using Lancir.Core.GridImages;
using Lancir.Core.Services;
using Lancir.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace Lancir.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>True when Windows started the app at sign-in (the startup entry passes <c>--startup</c>), false when the user opened it.</summary>
    public static bool LaunchedByWindowsStartup { get; private set; }

    /// <summary>Passed to the copy of the app that takes over after a restore, so it waits for this one to finish closing.</summary>
    public const string RestartArgument = "--after-restore";

    /// <summary>Starts a new copy of the app and closes this one (a restored backup takes effect at the start of the new copy).</summary>
    public static void RestartForRestore()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, RestartArgument) { UseShellExecute = false });
        }

        if (WindowBehavior.Current is { } behavior)
        {
            behavior.Exit();
        }
        else
        {
            Current.Shutdown();
        }
    }

    // "One Lancir at a time" means one per data folder: a copy started with LANCIR_DATA_DIR (testing, a portable setup)
    // uses other data and must not be taken for the first instance. The usual %AppData% folder gives one fixed name.
    private static readonly string InstanceId = Convert.ToHexString(
        System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(AppPaths.AppDataFolder.ToLowerInvariant())))[..10];

    private static readonly string SingleInstanceMutexName = @"Local\Lancir.SingleInstance." + InstanceId;
    private static readonly string ShowWindowEventName = @"Local\Lancir.ShowWindow." + InstanceId;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showWindowSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LaunchedByWindowsStartup = e.Args.Contains(StartupRegistration.StartupArgument, StringComparer.OrdinalIgnoreCase);

        // One Lancir at a time: two copies would both count playtime and fight over the database. A second launch (for
        // example from the Start menu while the first sits in the notification area) just asks the first to show itself.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance && e.Args.Contains(RestartArgument, StringComparer.OrdinalIgnoreCase))
        {
            // started by the previous run for a restore: wait for it to finish closing, then take over
            try
            {
                isFirstInstance = _singleInstance.WaitOne(TimeSpan.FromSeconds(20));
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true; // the previous run ended without releasing it: it is ours now
            }
        }

        if (!isFirstInstance)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowWindowEventName);
                signal.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                // the first instance is still starting up: nothing to wake yet
            }

            Shutdown();
            return;
        }

        _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        ThreadPool.RegisterWaitForSingleObject(
            _showWindowSignal,
            (_, _) => Dispatcher.BeginInvoke(() => WindowBehavior.Current?.Restore()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // A backup chosen for restoring is swapped in here, before anything opens the database.
        var restoreError = BackupService.ApplyPendingRestore();
        AppPaths.EnsureFoldersExist();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        AppLanguage.Apply(Services.GetRequiredService<SettingsService>().Load().Language);
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(AppLanguage.Culture.IetfLanguageTag)));

        using (var scope = Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            dbContext.Database.Migrate();
        }

        GameNameAppearance.Instance.Initialize(Services.GetRequiredService<SettingsService>());
        UiPreferences.Instance.Initialize(Services.GetRequiredService<SettingsService>());

        StartupRegistration.RepairPath(); // "start with Windows" must keep pointing at this exe if it was moved

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        mainWindow.ApplyStartState(); // "start minimized"
        if (restoreError is not null)
        {
            MessageBox.Show(Loc.F("Yedek geri yüklenemedi: {0}", restoreError), "Lancir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Services.GetRequiredService<BackupScheduler>().Start();

        Services.GetRequiredService<PlaytimeTracker>().Start();
        _ = Task.Run(() => ImageLoader.PruneThumbnails()); // keeps the thumbnail folder under its size limit

        // Pictures downloaded before compression existed (or before the setting was changed) are converted quietly in the
        // background, a little after startup so loading the library comes first.
        // It runs only when the image settings are new or changed (ImageOptimizer.NeedsRun); otherwise nothing is scanned.
        var optimizer = Services.GetRequiredService<ImageOptimizer>();
        if (optimizer.NeedsRun)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                await optimizer.RunAsync();
            });
        }

        // Clips (when their optimization is on): only clips that were not handled before, and not while a game is running, so
        // encoding never competes with a game for the processor and the graphics card.
        var clipOptimizer = Services.GetRequiredService<ClipOptimizer>();
        var tracker = Services.GetRequiredService<PlaytimeTracker>();
        if (clipOptimizer.NeedsRun)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                while (tracker.AnyRunning)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5));
                }

                await clipOptimizer.RunAsync();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WindowBehavior.Current?.Dispose();
        ClipStream.ShutDown();
        VideoStream.ShutDown();
        _showWindowSignal?.Dispose();
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // not the owner (a second instance that exited at startup)
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddDbContext<LibraryDbContext>(
            options => options.UseSqlite($"Data Source={AppPaths.DatabasePath}"),
            contextLifetime: ServiceLifetime.Transient,
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddHttpClient<ISteamGridDbClient, SteamGridDbClient>();
        services.AddHttpClient<ISteamStoreClient, SteamStoreClient>();
        services.AddHttpClient<ISteamLibraryClient, SteamLibraryClient>();
        services.AddHttpClient<ISteamAchievementsClient, SteamAchievementsClient>();
        services.AddHttpClient<SteamMediaCache>();
        services.AddHttpClient<ImageCacheService>();

        // Singleton so the Twitch OAuth access token IgdbClient caches actually gets reused
        // across calls instead of being re-fetched every time.
        services.AddHttpClient("Igdb");
        services.AddSingleton<IIgdbClient>(sp =>
            new IgdbClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("Igdb")));

        services.AddSingleton<SettingsService>();
        services.AddSingleton<GameLaunchService>();
        services.AddSingleton<PlaytimeTracker>();
        services.AddSingleton<ImageOptimizer>();
        services.AddSingleton<OriginalsRestorer>();
        services.AddSingleton<ClipOptimizer>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<BackupScheduler>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<PlayHistoryService>();
        services.AddSingleton<DebugLogService>();
        services.AddTransient<IMatchPickerService, MatchPickerService>();

        services.AddTransient<LibraryService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<ScanReviewViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<DebugConsoleViewModel>();

        services.AddSingleton<MainWindow>();
    }
}

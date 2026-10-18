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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);


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

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        Services.GetRequiredService<PlaytimeTracker>().Start();
        _ = Task.Run(() => ImageLoader.PruneThumbnails()); // keeps the thumbnail folder under its size limit
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ClipStream.ShutDown();
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

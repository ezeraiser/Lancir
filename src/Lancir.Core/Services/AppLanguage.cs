using System.Globalization;

namespace Lancir.Core.Services;

/// <summary>
/// The language of the app: the user's choice in Settings ("System", "en" or "tr"). "System"
/// follows the Windows display language when it is supported and falls back to English otherwise.
/// </summary>
public static class AppLanguage
{
    public const string System = "System";
    public const string English = "en";
    public const string Turkish = "tr";

    /// <summary>The language in effect: "en" or "tr".</summary>
    public static string Current { get; private set; } = English;

    public static bool IsTurkish => Current == Turkish;

    /// <summary>The culture used for dates and numbers shown in the UI.</summary>
    public static CultureInfo Culture => new(IsTurkish ? "tr-TR" : "en-US");

    /// <summary>Language word for Steam's store API.</summary>
    public static string SteamLanguage => IsTurkish ? "turkish" : "english";

    /// <summary>Country code for Steam's store API.</summary>
    public static string SteamCountry => IsTurkish ? "TR" : "US";

    /// <summary>Maps a stored setting to the language that actually applies.</summary>
    public static string Resolve(string? setting) => setting switch
    {
        English => English,
        Turkish => Turkish,
        _ => SystemLanguage(),
    };

    /// <summary>The Windows display language if the app supports it, otherwise English.</summary>
    public static string SystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Turkish ? Turkish : English;

    public static void Apply(string? setting)
    {
        Current = Resolve(setting);

        var culture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

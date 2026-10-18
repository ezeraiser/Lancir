using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Translates the app's own texts. The Turkish text is the key: in Turkish it is returned as is,
/// in English it is looked up in <see cref="EnglishStrings"/> (a missing entry falls back to the
/// Turkish text, so a forgotten translation shows Turkish instead of nothing).
/// </summary>
public static class Loc
{
    public static string T(string turkish) =>
        !AppLanguage.IsTurkish && EnglishStrings.Map.TryGetValue(turkish, out var english) ? english : turkish;

    /// <summary>Translates a format string, then fills in its {0}, {1} ... placeholders.</summary>
    public static string F(string turkishFormat, params object?[] args) =>
        string.Format(AppLanguage.Culture, T(turkishFormat), args);
}

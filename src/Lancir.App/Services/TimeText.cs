using Lancir.Core.Services;
using System.Globalization;

namespace Lancir.App.Services;

/// <summary>Turkish duration/date wording used by the library cards and the details panel.</summary>
public static class TimeText
{
    private static CultureInfo Turkish => AppLanguage.Culture;

    public static string Duration(long seconds) => seconds switch
    {
        <= 0 => Loc.F("{0} dk", 0),
        < 60 => Loc.T("1 dk'dan az"),
        < 3600 => Loc.F("{0} dk", seconds / 60),
        _ => Loc.F("{0} sa {1} dk", seconds / 3600, seconds % 3600 / 60),
    };

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", Turkish);

    public static string DateTimeText(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy HH:mm", Turkish);

    /// <summary>"Bugün", "Dün", "3 gün önce", otherwise the date.</summary>
    public static string Relative(DateTime utc)
    {
        var days = (DateTime.Now.Date - utc.ToLocalTime().Date).Days;
        return days switch
        {
            <= 0 => Loc.T("Bugün"),
            1 => Loc.T("Dün"),
            < 7 => Loc.F("{0} gün önce", days),
            _ => Date(utc),
        };
    }
}

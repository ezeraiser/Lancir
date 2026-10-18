using System.IO;
using System.Windows.Media.Imaging;
using Lancir.Core.Services;
using Wpf.Ui.Controls;

namespace Lancir.App.Services;

/// <summary>The icons a custom library can have: a built-in symbol, or a picture the user loads (kept as a small PNG in the data folder).</summary>
public static class LibraryIcons
{
    public static IReadOnlyList<SymbolRegular> Symbols { get; } =
    [
        SymbolRegular.Folder24, SymbolRegular.Library24, SymbolRegular.Star24, SymbolRegular.Heart24, SymbolRegular.PersonHeart24,
        SymbolRegular.Games24, SymbolRegular.XboxController24, SymbolRegular.Trophy24, SymbolRegular.Crown24, SymbolRegular.Diamond24,
        SymbolRegular.Flag24, SymbolRegular.Target24, SymbolRegular.Rocket24, SymbolRegular.Airplane24, SymbolRegular.Earth24,
        SymbolRegular.Sparkle24, SymbolRegular.Flash24, SymbolRegular.Fire24, SymbolRegular.Wand24, SymbolRegular.Lightbulb24,
        SymbolRegular.Bookmark24, SymbolRegular.Book24, SymbolRegular.Tag24, SymbolRegular.Pin24, SymbolRegular.Gift24,
        SymbolRegular.People24, SymbolRegular.Person24, SymbolRegular.Bot24, SymbolRegular.Shield24, SymbolRegular.Sport24,
        SymbolRegular.MusicNote124, SymbolRegular.Headphones24, SymbolRegular.Beach24, SymbolRegular.WeatherSunny24, SymbolRegular.Cloud24,
        SymbolRegular.Home24, SymbolRegular.Building24, SymbolRegular.Briefcase24, SymbolRegular.Cart24, SymbolRegular.Clock24,
        SymbolRegular.Hourglass24, SymbolRegular.Calendar24, SymbolRegular.Color24, SymbolRegular.Apps24, SymbolRegular.Key24,
    ];

    public static string IconsFolder { get; } = Path.Combine(AppPaths.AppDataFolder, "cache", "library-icons");

    /// <summary>Turns a picture file into the library's icon: a 128 px PNG in the data folder. Null when the file cannot be read as a picture.</summary>
    public static string? Import(string sourcePath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 128;
            image.UriSource = new Uri(sourcePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            Directory.CreateDirectory(IconsFolder);
            var target = Path.Combine(IconsFolder, $"{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(target);
            encoder.Save(stream);
            return target;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    public static void Delete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && Path.GetDirectoryName(path) == IconsFolder)
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

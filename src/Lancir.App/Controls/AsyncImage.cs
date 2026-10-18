using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lancir.App.Services;
using Lancir.Core.Services;

namespace Lancir.App.Controls;

/// <summary>
/// Attached properties that fill an <see cref="Image"/> or <see cref="ImageBrush"/> from a file path or web address
/// without blocking the UI: a picture seen recently appears at once from <see cref="ImageLoader"/>'s small cache,
/// anything else is decoded on a background thread and appears when ready. Replaces binding through a converter, which
/// decoded every picture on the UI thread (a visible stall when moving between games).
/// <code>&lt;Image ctrl:AsyncImage.DecodeWidth="320" ctrl:AsyncImage.Source="{Binding Path}" /&gt;</code>
/// Put DecodeWidth before Source: it is the width the picture is shown at, and pictures are decoded no wider.
/// </summary>
public static class AsyncImage
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached("Source", typeof(string), typeof(AsyncImage), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty DecodeWidthProperty =
        DependencyProperty.RegisterAttached("DecodeWidth", typeof(int), typeof(AsyncImage), new PropertyMetadata(0, OnChanged));

    /// <summary>For web pictures: keep a copy on disk so each is downloaded only once (achievement icons).</summary>
    public static readonly DependencyProperty CacheOnDiskProperty =
        DependencyProperty.RegisterAttached("CacheOnDisk", typeof(bool), typeof(AsyncImage), new PropertyMetadata(false, OnChanged));

    // Counts requests per element so a slow, outdated load can never overwrite a newer picture.
    private static readonly DependencyProperty VersionProperty =
        DependencyProperty.RegisterAttached("Version", typeof(int), typeof(AsyncImage), new PropertyMetadata(0));

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static int GetDecodeWidth(DependencyObject element) => (int)element.GetValue(DecodeWidthProperty);

    public static void SetDecodeWidth(DependencyObject element, int value) => element.SetValue(DecodeWidthProperty, value);

    public static bool GetCacheOnDisk(DependencyObject element) => (bool)element.GetValue(CacheOnDiskProperty);

    public static void SetCacheOnDisk(DependencyObject element, bool value) => element.SetValue(CacheOnDiskProperty, value);

    private static DependencyProperty? TargetProperty(DependencyObject element) => element switch
    {
        Image => Image.SourceProperty,
        ImageBrush => ImageBrush.ImageSourceProperty,
        _ => null,
    };

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (TargetProperty(element) is not { } target)
        {
            return;
        }

        var version = (int)element.GetValue(VersionProperty) + 1;
        element.SetValue(VersionProperty, version);

        var source = GetSource(element);
        if (string.IsNullOrWhiteSpace(source))
        {
            element.ClearValue(target);
            return;
        }

        var width = GetDecodeWidth(element);
        var cached = ImageLoader.TryGetCached(source, width);
        if (cached is not null)
        {
            element.SetValue(target, cached);
        }
        else
        {
            element.ClearValue(target); // never leave the previous game's picture showing while the new one loads
        }

        var folder = GetCacheOnDisk(element) ? AppPaths.AchievementIconsFolder : null;
        _ = LoadAsync(element, target, source, width, folder, version, cached);
    }

    private static async Task LoadAsync(
        DependencyObject element, DependencyProperty target, string source, int width, string? folder, int version, ImageSource? shown)
    {
        var image = await ImageLoader.LoadAsync(source, width, folder).ConfigureAwait(false);
        if (image is null)
        {
            return;
        }

        // Hand the result to the element's own UI thread explicitly (not relying on the caller's synchronization context).
        // Apply only if nothing newer was asked for in the meantime.
        var dispatcher = element is DispatcherObject owner ? owner.Dispatcher : Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            if ((int)element.GetValue(VersionProperty) == version && !ReferenceEquals(image, shown))
            {
                element.SetValue(target, image);
            }
        });
    }
}

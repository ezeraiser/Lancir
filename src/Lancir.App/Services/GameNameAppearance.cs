using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.Core.Services;

namespace Lancir.App.Services;

public sealed record FontWeightOption(string Key, string Label);

/// <summary>
/// App-wide look of game names (font, size, weight), edited in Settings and bound by the library
/// cards, list rows and details panel through the GameName*Style styles in App.xaml. Changes apply
/// live and are saved immediately.
/// </summary>
public sealed partial class GameNameAppearance : ObservableObject
{
    public static string DefaultFontLabel => Loc.T("Varsayılan");
    private const double DefaultSize = 14;

    private SettingsService? _settingsService;
    private bool _loading;
    private IReadOnlyList<string>? _fontOptions;

    public static GameNameAppearance Instance { get; } = new();

    private GameNameAppearance()
    {
        _selectedWeight = WeightOptions[0];
    }

    public IReadOnlyList<FontWeightOption> WeightOptions { get; } =
    [
        new("Default", Loc.T("Varsayılan")),
        new("Normal", Loc.T("Normal")),
        new("SemiBold", Loc.T("Yarı kalın")),
        new("Bold", Loc.T("Kalın")),
    ];

    /// <summary>Installed font names, "Varsayılan" first. Built on first use (enumerating fonts is slowish).</summary>
    public IReadOnlyList<string> FontOptions => _fontOptions ??=
        [DefaultFontLabel, .. Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomFont))]
    [NotifyPropertyChangedFor(nameof(CustomFontFamily))]
    private string _selectedFont = DefaultFontLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardFontSize))]
    [NotifyPropertyChangedFor(nameof(ListFontSize))]
    [NotifyPropertyChangedFor(nameof(DetailsFontSize))]
    [NotifyPropertyChangedFor(nameof(CardLineHeight))]
    [NotifyPropertyChangedFor(nameof(CardTwoLineHeight))]
    [NotifyPropertyChangedFor(nameof(ListLineHeight))]
    [NotifyPropertyChangedFor(nameof(ListTwoLineHeight))]
    private double _baseSize = DefaultSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardFontWeight))]
    [NotifyPropertyChangedFor(nameof(ListFontWeight))]
    [NotifyPropertyChangedFor(nameof(DetailsFontWeight))]
    private FontWeightOption _selectedWeight;

    public bool HasCustomFont => SelectedFont != DefaultFontLabel;

    public FontFamily? CustomFontFamily => HasCustomFont ? new FontFamily(SelectedFont) : null;

    public double CardFontSize => BaseSize;

    // A name may take two lines and no more: fixed line heights make the space it needs predictable (a tile reserves two lines so
    // all tiles stay the same height; a longer name ends in "…").
    public double CardLineHeight => Math.Round(BaseSize * 1.35);

    public double CardTwoLineHeight => CardLineHeight * 2;

    public double ListLineHeight => Math.Round((BaseSize - 1) * 1.3);

    public double ListTwoLineHeight => ListLineHeight * 2;

    public double ListFontSize => BaseSize - 1;

    public double DetailsFontSize => BaseSize + 4;

    public FontWeight CardFontWeight => WeightOrDefault(FontWeights.Normal);

    public FontWeight ListFontWeight => WeightOrDefault(FontWeights.SemiBold);

    public FontWeight DetailsFontWeight => WeightOrDefault(FontWeights.Bold);

    public void Initialize(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = settingsService.Load();

        _loading = true;
        try
        {
            SelectedFont = string.IsNullOrWhiteSpace(settings.GameNameFontFamily)
                ? DefaultFontLabel
                : settings.GameNameFontFamily;
            BaseSize = settings.GameNameFontSize is >= 8 and <= 40 ? settings.GameNameFontSize : DefaultSize;
            SelectedWeight = WeightOptions.FirstOrDefault(w => w.Key == settings.GameNameFontWeight) ?? WeightOptions[0];
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        SelectedFont = DefaultFontLabel;
        BaseSize = DefaultSize;
        SelectedWeight = WeightOptions[0];
    }

    partial void OnSelectedFontChanged(string value) => Save();

    partial void OnBaseSizeChanged(double value) => Save();

    partial void OnSelectedWeightChanged(FontWeightOption value) => Save();

    private FontWeight WeightOrDefault(FontWeight fallback) => SelectedWeight.Key switch
    {
        "Normal" => FontWeights.Normal,
        "SemiBold" => FontWeights.SemiBold,
        "Bold" => FontWeights.Bold,
        _ => fallback,
    };

    private void Save()
    {
        if (_settingsService is null || _loading)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.GameNameFontFamily = HasCustomFont ? SelectedFont : null;
        settings.GameNameFontSize = BaseSize;
        settings.GameNameFontWeight = SelectedWeight.Key;
        _settingsService.Save(settings);
    }
}

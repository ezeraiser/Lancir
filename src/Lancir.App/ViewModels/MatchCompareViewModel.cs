using Lancir.Core.Models;
using Lancir.App.Services;

namespace Lancir.App.ViewModels;

public sealed class MatchCompareViewModel
{
    public MatchCompareViewModel(
        string gameDisplayName,
        string? currentCoverImagePath,
        string? currentBackgroundImagePath,
        string? currentDescription,
        string? currentGenres,
        double? currentCommunityRating,
        DateTime? currentReleaseDateUtc,
        MatchPreview preview)
    {
        GameDisplayName = gameDisplayName;
        Preview = preview;

        NameField = new MatchCompareFieldViewModel(Loc.T("İsim"), gameDisplayName, preview.Name, isImage: false);
        CoverField = new MatchCompareFieldViewModel(Loc.T("Kapak Görseli"), currentCoverImagePath, preview.CoverImageUrl, isImage: true);
        BackgroundField = new MatchCompareFieldViewModel(
            Loc.T("Arka Plan Görseli"), currentBackgroundImagePath, preview.BackgroundImageUrl, isImage: true);
        DescriptionField = new MatchCompareFieldViewModel(Loc.T("Açıklama"), currentDescription, preview.Description, isImage: false);
        GenresField = new MatchCompareFieldViewModel(Loc.T("Tür / Etiketler"), currentGenres, preview.Genres, isImage: false);
        CommunityRatingField = new MatchCompareFieldViewModel(
            Loc.T("Topluluk Puanı"), FormatRating(currentCommunityRating), FormatRating(preview.CommunityRating), isImage: false);
        ReleaseDateField = new MatchCompareFieldViewModel(
            Loc.T("Çıkış Tarihi"), FormatDate(currentReleaseDateUtc), FormatDate(preview.ReleaseDateUtc), isImage: false);
    }

    private static string? FormatRating(double? rating) => rating?.ToString("F0");

    private static string? FormatDate(DateTime? date) => date?.ToString("dd.MM.yyyy");

    public string GameDisplayName { get; }

    public string HeaderText => Loc.F("'{0}' için her alanda hangi değerin kullanılacağını seçin. Bir karta tıklayarak Mevcut/Yeni arasında geçiş yapabilirsiniz.", GameDisplayName);

    public MatchPreview Preview { get; }

    public MatchCompareFieldViewModel NameField { get; }

    public MatchCompareFieldViewModel CoverField { get; }

    public MatchCompareFieldViewModel BackgroundField { get; }

    public MatchCompareFieldViewModel DescriptionField { get; }

    public MatchCompareFieldViewModel GenresField { get; }

    public MatchCompareFieldViewModel CommunityRatingField { get; }

    public MatchCompareFieldViewModel ReleaseDateField { get; }

    public IReadOnlyList<MatchCompareFieldViewModel> Fields =>
        [NameField, CoverField, BackgroundField, DescriptionField, GenresField, CommunityRatingField, ReleaseDateField];
}

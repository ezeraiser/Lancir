using CommunityToolkit.Mvvm.ComponentModel;

namespace Lancir.App.ViewModels;

/// <summary>
/// One row in the old-vs-new comparison screen (e.g. "İsim" or "Görsel"). Holds the current and
/// candidate values plus which side the user has picked; <see cref="IsImage"/> tells the view
/// whether to render the value as text or as a cover thumbnail.
/// </summary>
public sealed partial class MatchCompareFieldViewModel : ObservableObject
{
    public MatchCompareFieldViewModel(string label, string? oldValue, string? newValue, bool isImage)
    {
        Label = label;
        OldValue = oldValue;
        NewValue = newValue;
        IsImage = isImage;

        // Default to the new value when there is one to offer - that's the point of running the
        // search - but fall back to keeping the old value if the new source came back empty.
        _useNew = !string.IsNullOrWhiteSpace(newValue);
    }

    public string Label { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    public bool IsImage { get; }

    public bool HasNewValue => !string.IsNullOrWhiteSpace(NewValue);

    [ObservableProperty]
    private bool _useNew;
}

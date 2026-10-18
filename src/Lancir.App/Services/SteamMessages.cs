using Lancir.Core.Steam;

namespace Lancir.App.Services;

/// <summary>User-facing explanations of why Steam gave no game list.</summary>
public static class SteamMessages
{
    public static string For(SteamFetchStatus status) => status switch
    {
        SteamFetchStatus.Private => Loc.T("Oyun listesi gizli. Steam'de Profili Düzenle → Gizlilik Ayarları → \"Oyun ayrıntıları\" seçeneğini Herkese açık yapın."),
        SteamFetchStatus.InvalidKey => Loc.T("Steam API anahtarı reddedildi. Anahtarı kontrol edin."),
        SteamFetchStatus.ProfileNotFound => Loc.T("Steam profili bulunamadı. Profil adresini (steamcommunity.com/id/...) kontrol edin."),
        SteamFetchStatus.Failed => Loc.T("Steam'e ulaşılamadı. İnternet bağlantınızı kontrol edip tekrar deneyin."),
        _ => "",
    };
}

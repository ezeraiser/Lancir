# Lancir

A Windows game library launcher (WPF, .NET 8). It scans your game folders, matches games with Steam / SteamGridDB / IGDB
for covers and details, tracks playtime, and can import your Steam library, playtime and achievements.

## Build

Requires the .NET 8 SDK on Windows.

```
dotnet build Lancir.sln
dotnet test Lancir.sln
dotnet publish src/Lancir.App/Lancir.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/Lancir-win-x64
```

The published app needs the .NET 8 Desktop Runtime.

## Keys and settings

No keys are included in this repository. Everything is entered in the app under **Settings** and stored on your own
computer in `%AppData%\Lancir\settings.json`:

- **Steam** (optional): your profile address and a Steam Web API key from <https://steamcommunity.com/dev/apikey>, used for
  importing your library, playtime and achievements. The key is stored encrypted for your Windows account.
  Your Steam privacy setting "Game details" must be Public.
- **SteamGridDB API key** (optional): extra cover artwork.
- **IGDB (Twitch) client id and secret** (optional): a further cover and details fallback, from <https://dev.twitch.tv/console/apps>.

Without any keys, matching with the Steam store still works; the optional sources are simply skipped.

## Layout

- `src/Lancir.Core` - scanning, matching, Steam/IGDB clients, playtime tracking, database (SQLite, EF Core)
- `src/Lancir.App` - the WPF app
- `src/Lancir.Tests` - unit tests

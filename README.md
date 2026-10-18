# Lancir

A Windows game library launcher (WPF, .NET 10). It scans your game folders, matches games with Steam / SteamGridDB / IGDB
for covers and details, tracks playtime, and can import your Steam library, playtime and achievements.

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build Lancir.sln
dotnet test Lancir.sln
dotnet publish src/Lancir.App/Lancir.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/Lancir-win-x64
```

The published app needs the .NET 10 Desktop Runtime.

## Keys and settings

No keys are included in this repository. Everything is entered in the app under **Settings** and stored on your own
computer in `%AppData%\Lancir\settings.json`:

- **Steam** (optional): your profile address and a Steam Web API key from <https://steamcommunity.com/dev/apikey>, used for
  importing your library, playtime and achievements. The key is stored encrypted for your Windows account.
  Your Steam privacy setting "Game details" must be Public.
- **SteamGridDB API key** (optional): extra cover artwork.
- **IGDB (Twitch) client id and secret** (optional): a further cover and details fallback, from <https://dev.twitch.tv/console/apps>.

Without any keys, matching with the Steam store still works; the optional sources are simply skipped.

Your library, settings and caches live in `%AppData%\Lancir`. Set the `LANCIR_DATA_DIR` environment variable to use another
folder instead (to try the app on a copy of your data, or for a portable setup); each data folder gets its own single instance.

## Layout

- `src/Lancir.Core` - scanning, matching, Steam/IGDB clients, playtime tracking, database (SQLite, EF Core)
- `src/Lancir.App` - the WPF app
- `src/Lancir.Tests` - unit tests

## License

Copyright (c) 2026 ezeraiser. All rights reserved. The source is published for viewing; no permission to use, copy, modify or
distribute it is granted without the author's written permission (see [LICENSE](LICENSE)).

Third-party components (including the LGPL-licensed VLC libraries) keep their own licenses: see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Lancir is not affiliated with Valve, Twitch/IGDB, SteamGridDB or VideoLAN.

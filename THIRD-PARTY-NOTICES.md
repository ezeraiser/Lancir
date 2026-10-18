# Third-party notices

Lancir uses the open-source components below. Each keeps its own license and copyright; Lancir's own license
(see `LICENSE`) does not change theirs. The license of each package is the one declared in its NuGet metadata for the
version used here.

## Components that are shipped with the app

| Component | Version | License | Used for |
|---|---|---|---|
| [VLC media player libraries (libvlc, libvlccore, plugins)](https://www.videolan.org/vlc/) via `VideoLAN.LibVLC.Windows` | 3.0.24 | LGPL-2.1-or-later | playing the short clips of a game's store description |
| [LibVLCSharp](https://github.com/videolan/libvlcsharp) | 3.9.x | LGPL-2.1-or-later | .NET bindings to libvlc |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (and its Windows native library) | 2.88.9 | MIT | reading and writing the WebP thumbnail cache |
| [WPF-UI](https://github.com/lepoco/wpfui) | 4.3.0 | MIT | the app's Fluent look and icons |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.3.2 | MIT | MVVM helpers |
| Entity Framework Core, Microsoft.Data.Sqlite, Microsoft.Extensions.* (Microsoft) | 10.0.x | MIT | the library database and app plumbing |
| [System.Drawing.Common](https://github.com/dotnet/runtime) | 10.0.x | MIT | reading icons from game executables |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | 2.1.6 | Apache-2.0 | SQLite native bindings |
| [SQLite](https://www.sqlite.org/copyright.html) | - | Public domain | the database engine (inside the SQLitePCLRaw native library) |

Components used only for building and testing (not shipped): xUnit, Microsoft.NET.Test.Sdk, coverlet (MIT / Apache-2.0).

## LGPL components (libvlc and LibVLCSharp)

libvlc, its plugins and LibVLCSharp are licensed under the GNU Lesser General Public License, version 2.1 or (at your
option) any later version: <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>.

- Lancir uses them as separate, dynamically loaded libraries: the libvlc files are in the `libvlc` folder next to
  `Lancir.App.exe`, and LibVLCSharp is the separate `LibVLCSharp.dll`. They are not modified by Lancir. You may replace them
  with another build of the same version.
- The source code of VLC / libvlc is available from VideoLAN: <https://code.videolan.org/videolan/vlc> and
  <https://www.videolan.org/vlc/>. The source of LibVLCSharp is at <https://github.com/videolan/libvlcsharp>.
- VLC and the VLC cone are trademarks of VideoLAN. Lancir is not affiliated with or endorsed by VideoLAN.

## MIT License (applies to the MIT-licensed components above)

Each MIT-licensed component is Copyright (c) its respective authors and is provided under this license:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Apache License 2.0 (SQLitePCLRaw)

SQLitePCLRaw is licensed under the Apache License, Version 2.0: <https://www.apache.org/licenses/LICENSE-2.0>.

## Services and trademarks

Lancir talks to these services using their public APIs. It is not affiliated with, endorsed by or sponsored by any of them,
and their terms of use apply to the data you fetch (covers, descriptions, achievements, playtime):

- **Steam** (store, community and Web API) - Valve Corporation. Steam and the Steam logo are trademarks of Valve Corporation.
  Terms: <https://steamcommunity.com/dev/apiterms>
- **SteamGridDB** - <https://www.steamgriddb.com/> (cover artwork, with your own API key)
- **IGDB** - owned by Twitch Interactive, Inc. (cover and game details, with your own Twitch client credentials)

Game names, covers, screenshots and descriptions shown in the app belong to their respective owners and are fetched from
those services at your request; they are not part of this repository.

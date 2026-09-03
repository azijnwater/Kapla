# Kapla

Kapla is a compact Windows audiobook player designed to live on the desktop like a classic music utility. It keeps the player small, expands into a connected library/settings view, and uses the selected cover art to shape the accent color.

![Kapla player window](docs/kapla-window.png)

## What it does

- Plays authorized Kobo audiobook downloads while preserving their individual tracks and chapters.
- Connects to a Kobo account through the device-activation flow.
- Browses Kobo audiobook titles, covers, authors, narrators, series, descriptions, and progress.
- Sends listening progress for linked books back to Kobo when the account service accepts it.
- Shows download percentage, stage, byte totals, and track progress during imports.
- Remembers playback position, bookmarks, playback speed, appearance, window placement, and library sorting locally.
- Reads embedded metadata, artwork, and chapters from local M4B, M4A, MP3, and AAC files.
- Uses WPF's native `MediaElement` and Windows Media Foundation for playback.

Kapla is Kobo-first. Local file support is included for metadata testing and for audiobook files that the user already owns.

## Requirements

- Windows 10 or later.
- .NET Framework 4.8 Developer Pack for the direct build script, or a .NET SDK with the Windows desktop targeting pack for the project file.
- A Kobo account and audiobook entitlement for the Kobo connector.

## Build and run

From the repository root in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\outputs\Kapla.exe
```

The build creates a self-contained `outputs` folder with `Kapla.exe`, the launcher, and the required assets. `outputs` is generated and intentionally ignored by Git.

The included project file can also be built with a Windows-capable .NET SDK:

```powershell
dotnet build .\Kapla.csproj
```

## Tests

Run the regression suite with:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tests\run-tests.ps1
```

The tests cover playback timeline boundaries, chapter alignment, settings recovery, metadata extraction, Kobo author mapping, cover fallbacks, and library persistence. They use synthetic fixtures and do not need Kobo credentials.

## Kobo integration boundary

The Kobo connector uses account services advertised by the Kobo client. Those services are not a stable public SDK and can change without notice. Kapla only imports a title when the account response provides an authorized playable manifest. Titles marked as protected by KDRM or Adobe DRM remain available through Kobo's supported apps.

Kapla does not access protected app storage, decrypt Kobo files, bypass DRM, or embed an emulator. Users are responsible for complying with the terms that apply to their account, titles, and region.

Kobo is a trademark of its respective owner. Kapla is an independent project and is not endorsed by or affiliated with Kobo.

## Local data and privacy

Kapla stores its local library, settings, downloaded authorized media, and encrypted Kobo session under:

```text
%LOCALAPPDATA%\KoboNativePlayer
```

Session credentials are protected with Windows DPAPI for the current Windows user. Do not commit this directory, its contents, or downloaded audiobook files. The repository's `.gitignore` excludes common copies of this data.

## Repository layout

```text
App.cs                    Application entry point
MainWindow.cs             Compact player and expanded library UI
KoboClient.cs             Kobo activation, library, download, and sync client
KoboMetadata.cs           Kobo author/contributor metadata mapping
LocalAudiobookMetadata.cs Local file metadata and artwork reader
PlaybackTimeline.cs       Track/chapter timing calculations
Models.cs                 Library, Kobo, and player models
Assets/                   Figma-derived icons and bundled Inter font
Tests/                    Fixture-based regression suite
```

See [`NOTICE.md`](NOTICE.md) for font, design-asset, artwork, and trademark notes.

## License

No open-source license has been selected for this project yet. Add the intended license before publishing if you want others to reuse, modify, or redistribute the code.

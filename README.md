<p align="center">
  <img src="docs/kapla-window.png" alt="Kapla audiobook player" width="620">
</p>

<h1 align="center">Kapla</h1>

<p align="center"><strong>Your audiobooks. Your desktop. </strong></p>

<p align="center">
  A tiny, native Windows audiobook player for authorized Kobo audiobooks, built to sit quietly on your desktop while you listen.
</p>

<p align="center">
  <a href="https://github.com/azijnwater/Kapla/releases/latest">Download Kapla</a> ·
  <a href="https://github.com/azijnwater/Kapla/issues">Report an issue</a> ·
  <a href="CONTRIBUTING.md">Contribute</a>
</p>

## Why Kapla?

Kobo audiobooks are easy to buy but not always pleasant to keep open on a Windows desktop. Kapla gives them a focused home: a small old-school utility window, native playback, chapter-aware progress, cover art, and a library that expands only when you need it.

It is for listeners who want to:

- keep an audiobook visible without giving up their whole screen;
- resume Kobo listening from a lightweight Windows app;
- see the title, author, cover, chapters, and real total duration in one place;
- use a player that starts quickly and does not need a browser runtime;
- inspect, improve, and redistribute the software freely.

## Highlights

- **Kobo-first library** — connect through Kobo's device-activation flow and browse entitled audiobook titles.
- **Native Windows player** — WPF and Windows Media Foundation, with no Electron, emulator, or web dashboard.
- **Background playback** — keep listening while Kapla is minimized or the desktop is locked, with Windows system media controls for play, pause, seeking, and skipping.
- **Real audiobook controls** — chapters, previous/next chapter, 15/30-second skipping, speed control, volume, and resume position.
- **Two progress views** — track the current chapter or see progress across the whole audiobook.
- **Automatic progress sync** — Kapla queues supported Kobo updates in the background, retries temporary failures, and never pauses playback to sync.
- **Download feedback** — concurrent authorized imports show titles, stages, byte totals, and percentage progress.
- **Small by default** — a compact desktop utility that can expand into the library, Kobo connection, settings, or sleep timer.
- **Comfortable listening** — light and dark themes, optional cover art, remembered window position, and a sleep timer with end-of-chapter support.
- **Metadata that travels with the book** — title, author, narrator, series, description, artwork, duration, and chapters where Kobo or the file provides them.

## Download and install

Download the latest **portable Windows x64 ZIP** from [GitHub Releases](https://github.com/azijnwater/Kapla/releases/latest). Kapla does not need an installer.

1. Extract the ZIP to a folder you control.
2. Start `Kapla.exe` or `Launch-Kapla.cmd`.
3. Windows 10 or later with .NET Framework 4.8 is required.

The release includes a SHA-256 checksum. Current builds are unsigned, so Windows SmartScreen may show an additional warning for a first-time download.

## Connect Kobo

Open Kapla, expand the top section, choose **Kobo**, and select **Connect Kobo**. Sign in through the device-activation flow, then return to Kapla to browse your audiobook library.

Kapla only imports a title when Kobo returns an authorized playable download manifest. Protected titles that do not provide one remain available in Kobo's official apps.

## A note about Kobo compatibility

Kobo does not provide a stable public desktop audiobook SDK. Kapla uses the account services exposed by the official client flow, so Kobo can change or restrict those services without notice. Library access, authorized imports, and progress sync depend on what Kobo returns for your account, title, and region.

Kapla does not bypass DRM, decrypt protected app storage, or pretend to be an official Kobo app. Use it only with accounts, titles, and files you are authorized to access, and follow the terms that apply to them.

## Local files

The **+** action can add audiobook files you already own in M4B, M4A, MP3, or AAC format. Kapla reads embedded title, author, artwork, duration, and chapter metadata when present. Local files are a convenience for owned media and metadata testing; the library and account integration remain Kobo-first.

## Build from source

Kapla is intentionally dependency-light: there is no package manager, browser runtime, or third-party download required for the native build. On Windows with the .NET Framework 4.8 developer tools:

```powershell
.\Tests\run-tests.ps1
.\build.ps1
```

The executable and runtime assets are written to `outputs`.

To create the versioned portable package and checksum:

```powershell
.\package-release.ps1
```

The package is written to `release`. Generated output and per-user data are ignored by Git.

## Privacy and local data

Kapla stores settings, library state, downloaded authorized media, and the encrypted Kobo session under:

```text
%LOCALAPPDATA%\KoboNativePlayer
```

The Kobo session is protected with Windows DPAPI for the current Windows user. Credentials, account data, listening history, and audiobook files are not part of this repository. See [SECURITY.md](SECURITY.md) before reporting a security issue.

Kapla never receives your Kobo password. Session credentials are only sent to the trusted Kobo API endpoint that requires them; media and CDN downloads use an anonymous request path. Disconnecting Kobo clears the local session.

## Security

Kapla rejects HTTP, local, private-network, and malformed resource destinations. API credentials are scoped to explicit HTTPS endpoints, redirects are followed without credentials, and tokens are never written to logs. See [SECURITY.md](SECURITY.md) for the concise security model and reporting guidance.

## Contributing

Ideas, bug reports, compatibility notes, and pull requests are welcome. Please keep the utility-window feel, avoid committing personal Kobo data or downloaded media, and run the regression suite before submitting changes. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Kapla is free and open source under the [MIT License](LICENSE). You may use, copy, modify, publish, distribute, sublicense, and sell copies of the software, subject to the license notice.

Kapla is an independent project and is not affiliated with or endorsed by Kobo. Kobo is a trademark of its respective owner. See [NOTICE.md](NOTICE.md) for bundled fonts, design assets, artwork, and trademark notes.

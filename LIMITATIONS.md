# Current limitations

## TypeScript migration — BLOCKED

Moving this WPF application to TypeScript requires a major framework rewrite. Kapla remains a native C#/.NET Framework 4.8 WPF application so the existing lightweight Windows architecture and interface are preserved.

## Physical lock-screen controls — UNVERIFIED

Windows System Media Transport Controls are implemented for play, pause, seeking, and skipping. Kapla now requests the correct default SMTC interface IID for the .NET Framework WinRT projection, keeps the session enabled while a book is active, and publishes Kapla metadata/artwork. A local Windows runtime smoke test confirmed that the session can be created successfully. Playback was verified to continue while Kapla was minimized, and the application remained responsive.

The automated QA environment could not lock the Windows desktop or emit hardware media-button input. Windows' global media-session query also failed because its session-enumeration service was unavailable. A final manual check with `Win+L` and the device's media buttons is still required.

Manual acceptance check:

1. Start a downloaded audiobook and note its position.
2. Minimize Kapla, lock Windows with `Win+L`, and wait at least 15 seconds.
3. Confirm that playback continues and that the lock-screen media card shows the correct title and author.
4. From the lock screen, verify pause, play, seek, skip back, and skip forward, including any physical media buttons available on the device.
5. Unlock Windows and confirm that Kapla is responsive and displays the position reached on the lock screen.

## Fresh Kobo activation flow — UNVERIFIED

The connected Kobo library and sync state were exercised without exposing credentials. A complete disconnect and fresh account activation was not performed because it would intentionally remove the user's saved Kobo session. The disconnected and activation states still require a manual end-to-end check when reconnecting an account is acceptable.

Manual acceptance check (this intentionally removes the locally saved Kobo session):

1. In Kapla's Kobo panel, disconnect the current account and restart Kapla.
2. Confirm that the app remains disconnected after restart and that already downloaded audiobook files remain available.
3. Select **Connect Kobo**, complete Kobo's device activation in the browser, and return to Kapla.
4. Complete activation, sync the library, and confirm that entitled titles reappear without exposing credentials in the repository or logs.
5. Import or open one entitled title, restart Kapla, and confirm that the restored session and library sync still work.

## Modern .NET migration — ASSESSED, DEFERRED

Kapla should eventually move from .NET Framework 4.8 to a supported modern .NET LTS release while retaining WPF. This should be a dedicated migration after the manual playback and activation checks above, rather than being combined with structural refactoring.

The repository already uses an SDK-style project, but the migration still needs:

- a modern .NET SDK in the build and release environment;
- replacement of the direct .NET Framework compiler scripts with `dotnet build` and a modern test project;
- migration from `System.Web.Script.Serialization` and review of DPAPI and Registry compatibility;
- a supported Windows SDK projection for `SystemMediaTransportControls` instead of direct `.winmd` references;
- a new portable packaging decision: framework-dependent versus self-contained;
- full regression, Kobo activation, lock-screen media-control, and release-package verification.

As of 2026-09-04, .NET 10 is the appropriate LTS target. See Microsoft's [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) and [WPF migration guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/migration/).

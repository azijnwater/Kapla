# Current limitations

## TypeScript migration — BLOCKED

Moving this WPF application to TypeScript requires a major framework rewrite. Kapla remains a native C#/.NET Framework 4.8 WPF application so the existing lightweight Windows architecture and interface are preserved.

## Physical lock-screen controls — UNVERIFIED

Windows System Media Transport Controls are implemented for play, pause, seeking, and skipping. Playback was verified to continue while Kapla was minimized, and the application remained responsive.

The automated QA environment could not lock the Windows desktop or emit hardware media-button input. Windows' global media-session query also failed because its session-enumeration service was unavailable. A final manual check with `Win+L` and the device's media buttons is still required.

## Fresh Kobo activation flow — UNVERIFIED

The connected Kobo library and sync state were exercised without exposing credentials. A complete disconnect and fresh account activation was not performed because it would intentionally remove the user's saved Kobo session. The disconnected and activation states still require a manual end-to-end check when reconnecting an account is acceptable.

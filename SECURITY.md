# Security

Kapla never stores or receives your Kobo password. The device-activation flow returns session credentials, which Kapla protects locally with Windows CurrentUser DPAPI.

Credential-bearing requests are restricted to the explicit HTTPS Kobo API endpoint that needs them. The activation endpoint receives only the requests required for activation. Manifest, cover, audio-track, and CDN downloads use an anonymous path by default. Redirects are followed without Kobo credentials, and local/private destinations are rejected.

Disconnecting Kobo always clears the local DPAPI session and account state. Kapla does not claim a server-side revocation endpoint when Kobo does not expose a reliable supported one. Tokens and other session values are not written to logs or committed files.

Please do not include Kobo email addresses, activation codes, session files, access tokens, refresh tokens, downloaded audiobook files, or personal paths in an issue or pull request.

For a suspected security issue, open a private report through the repository's security contact rather than posting credentials publicly. If no private contact has been configured yet, leave the report out of the public issue tracker until the maintainer can provide one.

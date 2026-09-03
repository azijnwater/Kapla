# Contributing

Kapla is a small Windows desktop utility. Keep changes focused, preserve the compact utility-window layout, and avoid adding account data or downloaded media to the repository.

Before opening a pull request:

1. Run `powershell -ExecutionPolicy Bypass -File .\Tests\run-tests.ps1`.
2. Run `powershell -ExecutionPolicy Bypass -File .\build.ps1`.
3. Check that generated files remain ignored and that no personal paths or Kobo session data are included.

For Kobo integration changes, include a fixture-based regression test where possible. Do not require a contributor's Kobo credentials for tests.

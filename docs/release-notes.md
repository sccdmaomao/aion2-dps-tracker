Local DPS Tracker v0.9.2 improves character-name recovery when capture starts after entering the world.

- The app now explains how to recover a missed identity message: keep capture running and teleport once. Opening equipment does not resend that message.
- Detected and manually entered names are saved under %LOCALAPPDATA%\LocalDps, so extracting a new release no longer resets the saved fallback name.
- Existing profiles beside the executable remain readable when no saved user profile exists. Saved names do not prove party membership or replace the current combat-entity mapping.
- Added regression tests for profile compatibility, persistence across updates, explicit clearing and size limits.

First-time automatic detection still requires the server's local-character identity message. This update does not identify the local character from arbitrary nearby names.

Windows x64, .NET 8 Desktop Runtime and Npcap are required separately. Extract the whole ZIP and read START-HERE.txt. No account, AionFlex install, telemetry or automatic uploads.

The public build excludes the bulk third-party enemy-name catalog and accepts an optional local target-names.json instead. Target damage and defeat detection work without it; absent names display entity IDs. Public packages start with blank profiles and contain no real player caches or recordings.

Encounter timing uses a 15-second inactivity rule, including that grace period in DPS. A defeated target does not end the whole encounter automatically. Mid-instance identity/spawn coverage, unsupported party layouts, exact damage accuracy, pets and relay traffic remain provisional.

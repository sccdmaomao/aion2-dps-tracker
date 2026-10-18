Local DPS Tracker v0.9.1 makes the project branding consistent across the app, website, documentation and Windows package.

- App header, window title and executable metadata now say Local DPS Tracker.
- Launch the app with LocalDpsTracker.exe; release packages now use the LocalDpsTracker prefix.
- Updated website, setup instructions and app screenshot to match.
- Existing local settings and name caches remain compatible. Capture and DPS behavior are unchanged.

Windows x64, .NET 8 Desktop Runtime and Npcap are required separately. Extract the whole ZIP and read START-HERE.txt. No account, AionFlex install, telemetry or automatic uploads.

The public build excludes the bulk third-party enemy-name catalog and accepts an optional local target-names.json instead. Target damage and defeat detection work without it; absent names display entity IDs. Public packages start with blank profiles and contain no real player caches or recordings.

Encounter timing uses a 15-second inactivity rule, including that grace period in DPS. A defeated target does not end the whole encounter automatically. Mid-instance identity/spawn coverage, unsupported party layouts, exact damage accuracy, pets and relay traffic remain provisional.

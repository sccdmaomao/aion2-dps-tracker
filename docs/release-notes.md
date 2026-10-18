First public open-source release of Local DPS for Aion 2.

- Automatic adapter/port selection and character identity, with an optional in-app name fallback.
- Party-only damage/DPS, damage percentages, all detected members and target filtering.
- Automatic encounter timing/history and positive-to-zero HP defeat markers.
- Local encounter export, explicit diagnostic save, and offline replay.
- MIT source, build instructions, synthetic tests, and clean Windows release packaging.

Windows x64, .NET 8 Desktop Runtime and Npcap are required separately. Extract the whole ZIP and read START-HERE.txt. No account, AionFlex install, telemetry or automatic uploads.

The public build excludes the bulk third-party enemy-name catalog and accepts an optional local target-names.json instead. Target damage and defeat detection work without it; absent names display entity IDs. Public packages start with blank profiles and contain no real player caches or recordings.

Encounter timing uses a 15-second inactivity rule, including that grace period in DPS. A defeated target does not end the whole encounter automatically. Mid-instance identity/spawn coverage, unsupported party layouts, exact damage accuracy, pets and relay traffic remain provisional.

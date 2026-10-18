Local DPS Tracker v0.9.3 includes enemy names in every Windows download.

- Bundles 6,084 English NPC names from the MIT-licensed taengu/AION2-DPS-Meter catalog, with source attribution and the upstream license in NOTICE.md.
- Names are embedded in the app and resolved locally. No separate catalog download, account or runtime network request is needed.
- Existing optional target-names.json files can override or extend bundled names.
- Added regression checks for a fresh install, local overrides and fallback after invalid custom catalogs.

Start capturing, then teleport once to receive character identity and NPC spawn mappings. Targets whose spawn message was missed or whose template is absent from the catalog can still display as unknown. Names are English and may differ from your game's language.

Windows x64, .NET 8 Desktop Runtime and Npcap are required separately. Extract the whole ZIP and read START-HERE.txt. Public packages start with blank profiles and contain no real player caches or recordings. No telemetry or automatic uploads.

Encounter timing uses a 15-second inactivity rule, including that grace period in DPS. A defeated target does not end the whole encounter automatically. Mid-instance identity/spawn coverage, unsupported party layouts, exact damage accuracy, pets and relay traffic remain provisional.

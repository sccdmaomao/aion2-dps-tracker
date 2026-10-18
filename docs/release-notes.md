Local DPS Tracker 1.0 brings the streamlined local meter to GitHub downloads.

- Compact UI with play/stop capture icons beside the encounter timer, automatic character detection, a target selector and the party damage chart.
- One-click chart copying to the Windows clipboard, including the selected target, encounter totals and every party row. A checkmark confirms copying.
- Player names show item level and combat power when matching packets arrive. Missing values display a dash. Character-directory updates expand power recovery beyond recruiting-room messages.
- Locked column widths and order. Extra controls, encounter history, export and troubleshooting are available by right-clicking the chart and opening Options.
- Bundles 6,084 English enemy names for offline use, with source attribution and the upstream MIT notice in NOTICE.md.
- Remembers detected character names locally across updates; no manual name entry. No accounts, telemetry, external character API or automatic uploads.

Getting started: install the .NET 8 Desktop Runtime (Windows x64) and Npcap, extract the complete ZIP, launch LocalDpsTracker.exe, click the play icon, then teleport once to refresh character identity and NPC spawn data.

Item level often arrives during teleport. Combat power comes in separate updates, so teleport alone does not guarantee power data. Targets may remain unknown if their spawn message was missed or their template is not catalogued. Stats are session observations, not proof of party membership.

Encounters finish after 15 seconds without party damage; that grace period counts toward DPS time. A defeated target does not automatically end the whole encounter. Exact damage accuracy, unsupported protocol variants, pets and relay traffic remain provisional.

Validation: Windows build and offline capture/protocol/party/encounter/stat tests; recorded-data replay; compact UI and full-party chart-image checks. Public packages contain blank profiles and no player recordings or learned name caches.

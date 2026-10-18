# Local DPS for Aion 2

A local-only Windows DPS meter for Aion 2. Passive packet capture, automatic
game connection detection, and party filtering. No account, telemetry, automatic
uploads, or AionFlex installation required.

**[Download the latest Windows release](https://github.com/sccdmaomao/aion2-dps-tracker/releases/latest)**

![First launch and optional character setup](docs/images/startup.png)

## Run

1. Install [Microsoft .NET 8 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Install [Npcap](https://npcap.com/#download). If the app reports a missing
   `wpcap.dll`, enable Npcap's WinPcap API-compatible mode in its installer.
3. Extract the complete release ZIP to a writable folder and open `LocalDps.exe`.
4. Enter Aion 2 and click **Start capturing**. Adapter and game ports are automatic.

Your character name is detected when the server sends a local-character identity
message. If capture starts after that message, use the optional name field at the
top of the window. Detected names take priority over stale saved names. Start
capture before loading an area for the best chance of receiving identity and
target spawn messages.

## Features

- All detected party members, including idle members; up to five players total.
- Party-only damage, per-player DPS, damage percentage, and damage-share bars.
- Target filtering while capture continues for the whole party.
- Automatic encounter timing and the latest 50 encounters in memory.
- A target is marked **defeated** after a recent positive-to-zero HP transition.
- Local encounter exports, optional diagnostic recordings, and offline replay.
- Keep-on-top and optional damage/hit/share columns under **More**.

Damage share is a player's captured damage divided by all captured party damage
in the selected encounter and target. It is not a percentage of enemy maximum HP.

An encounter starts on the first verified party hit and finishes after **15 seconds
without damage**. That ending grace period counts toward elapsed time and DPS.
All targets use the encounter clock, with a one-second minimum for DPS division.
A defeated individual target does not end the whole encounter automatically.
Long boss-phase gaps can split an encounter.

## Target names

The public build includes the entity-to-template decoder but no bulk enemy-name
database. Unmapped targets display `Unknown target <entity ID>`; damage and
defeat detection still work.

To supply names, place a UTF-8 `target-names.json` next to `LocalDps.exe`. It is a
JSON object mapping **NPC template IDs**, not changing combat entity IDs, to
display names. See [the example](examples/target-names.example.json); its IDs and
names are illustrative, not a real game catalog. Use only data you have permission
to use. The app reads the file locally at startup, with a 2 MiB / 20,000-entry limit.
An invalid catalog is ignored and reported under Troubleshooting.

## Privacy

Capture starts only when requested. There are no outbound HTTP clients, accounts,
telemetry endpoints, or automatic uploads. The app does not inject into the game
or read game process memory.

Character fallback: `track-people.json` beside the executable. Learned names:
`%LOCALAPPDATA%\LocalDps\known-character-names.json`. Neither file grants party
membership by itself. Saved encounters go to `%LOCALAPPDATA%\LocalDps\encounters`.

**More → Troubleshooting → Save diagnostic recording** explicitly saves raw
game traffic to a file you select. The in-memory diagnostic ring is bounded to
two minutes / 16 MiB. Recordings can contain character names and identifiers;
do not attach them to public issues. Public source and releases contain no real
player profiles or recordings. Unicode protocol tests use substituted identities.

## Build and test

Windows x64, PowerShell, and the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
are required to build. There are no third-party NuGet dependencies.

```powershell
./scripts/Build.ps1
```

This builds, runs protocol/capture-reassembly/identity/party/encounter tests,
publishes a framework-dependent app, and creates a clean release ZIP under
`artifacts/`. Npcap and a running game are not needed for the offline self-tests.
Npcap is required for live capture.

For a build without packaging:

```powershell
dotnet build src/LocalDps/LocalDps.csproj -c Release
$test = Start-Process ./src/LocalDps/bin/Release/net8.0-windows/LocalDps.exe `
  -ArgumentList '--self-test' -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Self-tests failed' }
```

The build script creates a clean release ZIP and SHA-256 checksum. Maintainers
publish these assets on the GitHub Releases page. Automated CI is not enabled yet.

## Status and limits

This is a working prototype tested against local recordings, not an independently
verified benchmark of exact in-game damage. Protocol updates can break decoding.
Two-person join/leave layouts and five-person periodic recovery have checks;
unsupported roster layouts fail closed. Mid-instance capture may miss identity
or spawn data. Far-away members' damage is unavailable when the server does not
send it. Pets, relay traffic, and other untested layouts remain provisional.

See [protocol notes](docs/protocol.md), [contributing](CONTRIBUTING.md), and
[data/dependency notices](NOTICE.md). Original source is [MIT licensed](LICENSE).

---
name: aion2-contribute
description: Develop and validate general changes in aion2-dps-tracker using its Windows build, source layout and local-data boundaries. Use the dedicated protocol, UI or release skill for those focused tasks.
---

# Contribute to Local DPS Tracker

Read [CONTRIBUTING.md](../../../CONTRIBUTING.md) and the relevant sections of
[README.md](../../../README.md). Work from the repository root.

## Source map

- `src/LocalDps/` is the public, authoritative .NET 8 WPF application. Older
  `outputs/` trees and ignored `work/` helpers are not maintained source and may
  not exist in another contributor's checkout.
- `Capture/` reconstructs passive TCP traffic. `Protocol/` decodes records.
- `CombatScope.cs` gates self/party attribution. `PartyHitAttribution.cs` handles
  delayed mappings; `EncounterTracker.cs` owns encounter timing/history.
- `Meter.cs` aggregates damage and view rows. `MainWindow.xaml` and its code-behind
  own the UI, target selection and clipboard rendering.
- `Data/` contains embedded catalogs/rules; provenance belongs in `NOTICE.md`.
- `docs/site/` is the static Pages site, deployed separately from the Windows app.

## Change and validate

Inspect the affected code and existing `*SelfTests.cs` before editing. Keep source,
setup instructions and user-visible claims consistent. Add synthetic regressions
for behavior fixes; visual/text changes usually need a build and layout check.

For a focused local check on Windows x64 with the .NET 8 SDK:

```powershell
dotnet build src/LocalDps/LocalDps.csproj -c Release
$test = Start-Process ./src/LocalDps/bin/Release/net8.0-windows/LocalDpsTracker.exe `
  -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
if ($test.ExitCode -ne 0) { throw 'Offline self-tests failed' }
```

`./scripts/Build.ps1` also publishes and packages the app. Offline checks need
neither Npcap nor a running game. If Windows/.NET is unavailable, report the
validation limit instead of claiming the app was tested.

Keep runtime processing local. Use invented identities and substituted Unicode
names in public tests. Preserve the deny-by-default `.gitignore`; review new files
explicitly so recordings, personal caches and research artifacts stay private.
Check `NOTICE.md` before adding third-party data or code.

Report behavior changed, checks run and remaining limits. Version bumps, releases
and website publication belong to the requested publication scope, not every fix.

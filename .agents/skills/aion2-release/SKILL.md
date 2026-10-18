---
name: aion2-release
description: Prepare or publish a Local DPS Tracker Windows release, validate its clean ZIP and checksum, and verify GitHub CI and related Pages updates.
---

# Prepare or publish a release

Read [CONTRIBUTING.md](../../../CONTRIBUTING.md),
[NOTICE.md](../../../NOTICE.md) and `scripts/Build.ps1`. Match the requested scope:
preparing a package does not itself request a push, tag, GitHub release or deploy.
For a requested publication, finish and verify those steps within that scope.

## Version and build

Use a three-part semantic version in `src/LocalDps/LocalDps.csproj`, matching the
window title/version text in `MainWindow.xaml`. Write current release notes in
`docs/release-notes.md`; update setup/site claims when the release changes them.
The project is framework-dependent Windows x64, .NET 8 WPF. Npcap and the .NET
Desktop Runtime are installed separately, not redistributed in the package.

Run `./scripts/Build.ps1` on Windows x64 with the .NET 8 SDK. It builds, runs the
offline tests, publishes and creates `artifacts/LocalDpsTracker-VERSION-windows-x64.zip`
plus `.sha256`. Offline tests need neither a live game nor Npcap.
Use a fresh checkout/output location when a running app locks publish files;
preserve the user's active capture.

## Inspect the artifact

The script uses fresh staging and an explicit file allowlist. Preserve it.
Verify the ZIP contains the four app runtime files, blank `track-people.json`,
empty `learned-character-names.json`, `LICENSE`, `NOTICE.md` and `START-HERE.txt`.
Do not sweep a working install, `work/`, `outputs/` or old staging directories
into a release. No raw recordings, personal caches, decompiled code, third-party
application binaries, Npcap driver/DLLs or debug symbols belong in these assets.

The English target catalog is embedded; an external catalog is optional. Preserve
its source/commit/digest and upstream license in `NOTICE.md`. Do not replace it
with a private table lacking redistribution provenance.

Check the SHA-256 file against the ZIP and ensure final binary metadata reflects
the release version and intended source commit. Build final assets from that
commit; source changes after packaging require rebuilding the affected artifact.

## Publish and verify, when requested

Resolve the repository from its actual Git remote. Stage reviewed public paths,
preserve unrelated changes, and follow any existing contributor/author settings.
Create the new `vVERSION` release against the exact final commit and upload the
ZIP/checksum. Use `--notes-file docs/release-notes.md` with GitHub CLI to preserve
multiline release text. Keep historical releases/tags intact.

Verify the public release is the intended latest/non-draft/non-prerelease release,
with the expected assets and matching published digest. Confirm the relevant
`Build and test` run for the exact commit completes successfully. Fix failures
before reporting success.

Pages serves only `docs/site/`; README screenshots and private artifacts are not
automatically included. Its workflow runs for site/workflow changes or manual
dispatch. When a site update is in scope, verify its deployment and live content,
including the versioned download label and play/teleport instructions. Follow
actual progress with bounded waits; report failures or remaining external blockers
without claiming CI or deployment passed.

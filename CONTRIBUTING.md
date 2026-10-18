# Contributing

Build with `./scripts/Build.ps1` on Windows and keep the offline tests passing.
Small fixes and protocol evidence are welcome through issues and pull requests.

For a protocol change, explain the record boundary, opcode, fields and bounds,
and what observation validates the behavior. Add a small synthetic regression
test. Seeing a character's name, healing, damage, or nearby presence does not
establish party membership. Keep persistent character IDs separate from combat
entity IDs; reject ambiguous or unsupported membership layouts.

Do not commit real packet captures, player names/identifiers, private account
data, caches, third-party binaries/decompiled source, or bulk game databases.
Use substituted Unicode names and invented persistent IDs in tests. Any added
data must have a stated source and permission for redistribution.

Keep the runtime local-only. Features that transmit user data require a separate,
explicit design discussion; the current app has no upload or telemetry path.

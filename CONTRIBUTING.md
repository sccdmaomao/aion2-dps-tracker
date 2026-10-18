# Contributing

Build with `./scripts/Build.ps1` on Windows and keep the offline tests passing.
Small fixes and protocol evidence are welcome through issues and pull requests.

## Agent skills

Repository skills live in `.agents/skills/`. Use the skill that matches the task:

- [`aion2-contribute`](.agents/skills/aion2-contribute/SKILL.md): source layout, local development and validation.
- [`aion2-protocol`](.agents/skills/aion2-protocol/SKILL.md): capture, framing, identity, party scope, stats and damage decoding.
- [`aion2-ui`](.agents/skills/aion2-ui/SKILL.md): WPF layout, capture icons and clipboard chart images.
- [`aion2-release`](.agents/skills/aion2-release/SKILL.md): clean Windows packages, GitHub releases and Pages verification.

For agents that support skill invocation, request one by name, for example
`$aion2-protocol investigate missing combat power`. Other contributors can read
the same files as focused workflow guides. Skills describe how to work here;
they do not authorize publishing a release or transmitting player data.

## Protocol and data contributions

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

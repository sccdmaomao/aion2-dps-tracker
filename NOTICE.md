# Data and dependencies

The original application source in this repository is licensed under MIT.

Aion 2 and its game names belong to their respective owners. This project is
independent and is not affiliated with or endorsed by the game publisher.

Protocol layouts were investigated using passive local recordings and static
examination of an installed AionFlex build. The parser here is an independent
implementation. No third-party application binaries, decompiled source, raw
player recordings, character-name caches, credentials, or accounts are distributed.

`src/LocalDps/Data/skill-rules.json` contains numeric skill classification facts
used to exclude known healing and non-damage effects. Its source and source-file
digests are recorded in the JSON. Those classifications remain provisional;
they are not an independent damage-accuracy benchmark. No skill descriptions,
icons, names, or full third-party skill database are included.

The bulk enemy-name table used during private development is not included.
The public embedded target catalog is empty. Users may provide a local
`target-names.json` that they have permission to use; see the README. This
repository's MIT license does not grant rights to independently supplied data.

Npcap is a separately installed dependency governed by its own license:
https://npcap.com/#license. Its installer, DLLs, and driver are not redistributed.
Microsoft .NET is also installed separately.

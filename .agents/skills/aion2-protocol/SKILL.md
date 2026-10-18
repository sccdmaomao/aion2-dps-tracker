---
name: aion2-protocol
description: Investigate or change Aion 2 packet capture and decoding in this repo, including framing, character identity, party membership, combat stats, targets and DPS attribution.
---

# Work on Aion 2 protocol decoding

Read [CONTRIBUTING.md](../../../CONTRIBUTING.md) and
[docs/protocol.md](../../../docs/protocol.md). Treat protocol notes and previously
decoded samples as evidence with a defined scope, not a complete game specification.

## Follow the existing lanes

Trace the affected path through `src/LocalDps/Capture/`,
`Protocol/IdentityRecordStream.cs`, `Protocol/GameProtocolDecoder.cs`,
`CombatScope.cs` and `PartyHitAttribution.cs` as appropriate.

Identity/social records use a width-aware framing lane; combat has a separate
legacy lane. Inspect their length calculations before changing boundaries.
Preserve server-to-client direction checks, TCP assembly and bundle limits.
Do not interpret arbitrary embedded opcode-looking bytes as authoritative records.

## Preserve attribution boundaries

- A nearby name, hit, class, room listing or stat is not party membership.
- Keep persistent character IDs/home servers separate from transient combat
  entity IDs. Reject duplicate or conflicting name mappings.
- The game party limit is five players: self plus up to four peers. Supported
  roster changes outrank delayed recovery messages. Preserve leave, reset,
  entity-reuse and same-chunk membership behavior.
- Character-name caches are metadata only. Out-of-range members can retain a
  name without a combat entity; missing damage is not a measured zero.
- Item level and power arrive through different messages. Inspect
  `CharacterStatsDecoder.cs`, `CharacterNameDirectory.cs` and
  `RoomCharacterNames.cs`; preserve their identity checks and unavailable values.
- Resolve NPC combat entities through observed template mappings before applying
  the target catalog. Despawning alone does not prove death.

Keep existing damage/healing classification and encounter timing semantics unless
the task explicitly changes them. Inspect `SkillRules.cs` and `EncounterTracker.cs`
before changing totals or DPS denominators.

## Establish and test the change

Record opcode, complete record boundaries, field encoding, length/range checks,
identity association and the observation supporting each interpretation. Distinguish
an observed field from an inferred meaning; compare stats to visible game values
when needed. Do not select a plausible-looking integer solely because it is near
the expected number.

Prefer existing private recordings for reproduction. When fresh evidence is
needed, capture only relevant game traffic locally and coordinate player actions
after capture is active. A missed action is not a validated sample. Keep raw data
and reverse-engineering output out of commits, public issues and release assets.

Add small synthetic tests to the existing self-test suite, registered in
`SelfTests.Run()`. Exercise the relevant failure cases: truncation, variable-width
prefixes, split records, invalid fields, duplicate identities, late mappings and
session reset. Use invented IDs/names; do not rely on a developer's recordings,
adapter, server address or installed third-party software.

Run the Windows build and offline tests described in `CONTRIBUTING.md`/README.
For live validation, report which actions and messages were actually observed,
and keep unsupported variants unavailable rather than broadening party scope.

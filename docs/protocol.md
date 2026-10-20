# Decoder boundaries

The capture path is Npcap → Ethernet/IP/TCP validation → directional TCP stream
reassembly → combat and identity lanes → verified-party attribution → encounters.
Buffers, queues, decompression, directory lookup and roster parsing are bounded.
TCP retransmissions must not duplicate damage.

## Framing

Combat currently uses the legacy `declared - 3` physical-length hypothesis.
Identity/social records use `varint width + declared - 4`. They are separate
because recorded multi-byte lengths established different behavior in the
previous combat-only walk. Identity is observed only server-to-client. Both
lanes can walk the observed raw-LZ4 `FF FF` bundles with size/depth limits.

## Identity and membership

An anchored `33 36` supplies the local entity and strict UTF-8 name. Nearby
`44/45 36` and selected `04 8D` records supply names without granting membership.
Strong local identity takes priority over a saved name.

Supported explicit two-person party records: `0D 92` subtype 3/count 2 joins,
`00 92` supported complete count-1 replacement removes a peer, and exact
`13 92` body `00 00` clears on self-leave. Unsupported explicit snapshots block
periodic startup recovery. `1C 92` recovers up to four distinct current peers.

Persistent character ID, home server and class bind learned names. A bounded
`05 8A` directory lookup or recruiting-room `02 97` may supply name metadata;
neither establishes membership. A name must map to exactly one observed entity.
For a complete five-player party, self may be inferred only after all four
current peers are independently mapped and the bounded `0E 92` actor set has
exactly those four actors plus one remaining actor, with own persistent-name
metadata already known. Contradictory names invalidate the inference.

Up to two minutes of unresolved full-party candidate hits can be held in a
bounded queue. They count only after identity proof, within the same membership
epoch. Late hits are assigned to encounters by event timestamps and may merge
intervals. Membership changes discard unresolved candidates.

## Damage, targets and HP

Direct `04 38`, periodic `05 38` and batch `3D 38` are decoded with explicit
bounds. Numeric exclusions filter known non-damage/healing effects; unknown
skills are retained, so accuracy remains provisional.

Direct records can now walk `01 00` continuation markers at the next parsed
tail boundary, including immediately after damage or its leading tail field.
Each continuation must pass a side-effect-free probe using the existing direct
hit layout, numeric bounds and supported complete extra-hit count (0 through 10).
A terminal `01 00` alone is still an ordinary tail. The decoder never searches
unknown trailers for embedded markers; invalid leading hits stop the walk, and
a malformed continuation does not discard preceding hits. At most 64 hits are
decoded per direct record. Each hit retains its own actor, target, skill and
counter, inherits the enclosing timestamp, and independently passes skill and
verified-party filtering. Existing damage aggregation, framing and timing rules
are unchanged.

The continuation marker is a protocol lead from the
[A2Tools parser comparison](https://github.com/taengu/A2Tools-DPS-Meter/blob/08b249d49ef18be9ac9b41d8dd35596ae99aacfd/src-tauri/src/capture/stream_processor.rs#L1443).
This implementation extends this repository's existing parser and uses invented
fixtures; no upstream implementation or capture fixtures were copied. Existing
private recordings did not reproduce a validated chained hit, so synthetic
coverage establishes parser behavior, not parity with the game's combat analysis.
A fresh paired encounter is still needed to measure recovered damage in game.

NPC spawns `40/41/42 36` bind combat entities to template IDs. The public build
ships no bulk name table; an optional local catalog maps templates to names.

The observed exact-width HP record is `00 8D`, entity varint, `02 01 00`, HP:u32
little endian, and an opaque four-byte trailer. A positive-to-zero transition
within 30 seconds establishes a defeat marker. Zero without a prior positive
reading is unconfirmed. A 250 ms receive-time skew allows a final hit delivered
just after zero HP; later positive HP prevents old-life reuse. `42 36` removal
alone is not a death signal. HP values above 500,000,000 and other layouts are
unsupported. A confirmed defeat freezes that target's displayed timer and DPS
at the zero-HP timestamp. All-target totals freeze when every captured target
in the fight has a confirmed death; other living targets continue independently.
Durations retain the shared encounter start and a one-second DPS denominator
floor. The existing 250 ms final-hit skew can add killing damage without extending
the frozen timer; a confirmed new life starts a new fight. A new target after a
fully defeated fight also starts a fresh fight inside the idle window. When no
death is confirmed, the 15-second idle rule remains the fallback. Packet capture
stays active for subsequent fights. DPS is rounded to whole numbers for display
only; calculations retain their full precision.

## Validation scope

Tests cover synthetic TCP loss/reordering/retransmission/wrap, split framing,
Unicode names, party replacement/clear/startup, name-cache isolation, complete
and ambiguous five-player sets, late attribution, target remapping, HP zero and
revival, encounter timing and target/player DPS. Private development recordings
also exercised real game traffic, but are not distributed and are not an
independent comparison against game-server damage totals.

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
Its body is 63 bytes plus the optional guild-name byte count at offset 48,
with persistent character ID (UInt48 LE), home server (UInt16 LE) and class
(UInt32 LE) at offsets 0, 6 and 8. A five-player dungeon capture repeatedly
showed two peers with final byte 1, one with 2 and one with 3. These are accepted
for that complete layout. The trailer's meaning is unknown.

Persistent character ID, home server and class bind learned names. A bounded
`05 8A` directory lookup or recruiting-room `02 97` may supply name metadata;
neither establishes membership. A name must map to exactly one observed entity.
For a complete five-player party, self may be inferred only after all four
current peers are independently mapped and the bounded `0E 92` actor set has
exactly those four actors plus one remaining actor, with own persistent-name
metadata already known. Contradictory names invalidate the inference.

Player rows retain the raw roster/directory class ID as display metadata. Self
uses the anchored local-character job field; peers use identity-checked room or
directory records, or a unique learned class matching a current party member's
persistent ID, home server and name. Conflicting cache classes remain unavailable.
The observed roster encoding groups the eight supported classes into four-value
blocks from 5 through 36 (Gladiator, Templar, Ranger, Assassin, Spiritmaster,
Sorcerer, Cleric, Chanter), as documented in the
[reference roster observations](https://github.com/taengu/A2Tools-DPS-Meter/blob/08b249d49ef18be9ac9b41d8dd35596ae99aacfd/src-tauri/src/entity/job_class.rs#L44).
These are not damage-skill prefixes. Unsupported IDs
show no icon. Official NC class emblems are embedded for offline display, with class-name tooltips,
and do not grant capture scope. Accepted encounter rows preserve their class after
party changes along with their captured names and equipment stats.

Up to two minutes of unresolved candidate hits can be held once at least one
teammate is confirmed. A later unique combat ID can recover hits only for a
teammate whose membership was already confirmed when the hit arrived. Additions
can keep earlier candidates for existing teammates. A removal or reset discards
unresolved hits. The full-party self fallback still requires the same membership
throughout. Recovered hits use their original timestamps and may merge encounter
intervals.

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

NPC spawns `40/41/42 36` bind combat entities to template IDs. Boss-only tracking
requires a complete spawn in the existing target layout and a template marked
as a boss in the bundled catalog. The boss test runs before party attribution
and encounter aggregation, so a delayed party mapping keeps the classification
and target name observed when the hit arrived. Other hits and unmapped target
IDs do not start fights or contribute damage. Local name overrides affect labels.
See [the data notice](../NOTICE.md) for boss-classification provenance.

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
the frozen timer. Later damage to the defeated life is discarded, including
DoT ticks received while another boss is still alive; positive HP is required
before the same combat entity can start a new life. A new target after a
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

## Observed party death counts

Party HP updates use the width-aware server-to-client lane: `1B 92`, entity,
current HP and maximum HP as varints, followed by 25 opaque bytes. The bounded
shape was present in 283 updates across three private recordings. Zero-HP death
transitions were absent from those recordings; the transition interpretation is
supported by the [reference parser's `1B 92` handling](https://github.com/taengu/A2Tools-DPS-Meter/blob/08b249d49ef18be9ac9b41d8dd35596ae99aacfd/src-tauri/src/capture/stream_processor.rs#L591), and synthetic tests
exercise death, resurrection and duplicates. Live party-death validation remains
outstanding. Other health layouts are not guessed.

Only currently verified self/party actors can establish a positive-HP baseline
or death. A recent (at most 30 seconds) positive-to-zero transition counts once;
repeated zero updates do not, and positive HP permits another death. Unknown
initial zero, stale or out-of-order health, unsupported frames, entity/name reuse
and membership changes do not manufacture deaths. Ordered updates in one TCP
chunk may share a receive timestamp. Counters are local observations and may miss
deaths when health updates are not sent (for example, out-of-range members).

Skull badges show observed deaths within the selected encounter's start/end,
shared across target filters, in full view, compact view and copied charts.
Unmapped identities show a dash. Captured death-only members remain in finished
fight history after leaving. Health observations never authorize damage capture,
change DPS, start encounters or end enemy encounters. New capture/reset clears
baselines and counts; stored death events are bounded to 4,096 per session.

Local DPS Tracker 1.1.2 freezes target DPS on confirmed defeat and displays whole-number DPS.

- Confirmed target death immediately freezes that target’s timer and DPS. All-target totals finish when every captured target has a confirmed death; living targets continue independently.
- New targets after a completed fight start a fresh encounter. Delayed identity recovery cannot merge a finished fight into the next one, and the existing final-hit receive-time allowance preserves killing damage without extending the timer.
- DPS is rounded to whole numbers in full view, compact view and copied charts. Calculations retain full precision.
- Added bounded decoding of direct hits chained inside one combat record. Every hit retains its own actor, target, skill and counter and passes the existing skill and verified-party filters. Unsupported trailers are not scanned for hits.

Keep capture running and teleport once if the character name says “waiting for automatic confirmation.” A saved name alone does not establish the current combat identity. Install .NET 8 Desktop Runtime (Windows x64) and Npcap separately, and extract the entire ZIP before launching.

Validation: Windows build and offline tests, including chained-record splits, retransmissions, malformed data, party filtering, confirmed defeat, final-hit timing, multiple targets, respawn and late identity recovery. Fifteen private recordings retained the same hit and damage totals after the chained-hit change. Synthetic full/minimum/compact and copied-chart checks verified whole-number DPS and frozen defeat timers. Live scarecrow tracking resumed after teleport refreshed character identity. Existing recordings did not reproduce a validated chained hit, so in-game damage parity remains unverified.

The release remains local-only and includes the licensed English target catalog. Assets contain blank profiles and no player recordings, learned names or window preferences. Summon ownership and other unsupported protocol variants remain provisional.

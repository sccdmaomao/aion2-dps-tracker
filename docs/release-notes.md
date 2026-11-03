Local DPS Tracker 1.3.0 adds skull badges with observed party deaths per fight.

- Full view, compact view and copied charts show a skull and death count beside each party member’s name. Unmapped combat identities show a dash.
- Counts follow the selected fight, remain the same across target filters, and start fresh for the next encounter. Finished fights retain death-only party members after they leave.
- Only a recent positive-to-zero HP transition from a verified self/party actor counts. Repeated zero updates do not add deaths; resurrection permits another death. Party changes, entity/name reuse, stale HP, malformed records and out-of-order updates cannot manufacture a count.
- Counters are based on locally received HP updates. They may miss deaths when updates are not received, for example from out-of-range teammates. Deaths do not change damage capture, DPS or enemy defeat timing.

Validation: clean Windows build and offline regression tests for duplicate suppression, resurrection, same-chunk updates, nearby-player exclusion, malformed/stale health, party departure, preserved history, identity reuse and capture reset. Synthetic full/minimum/compact and copied-chart renders verified the badges. Three private recordings contained 283 supported HP updates but no player death transitions: in-game death-transition validation remains outstanding.

Install the .NET 8 Desktop Runtime (Windows x64) and Npcap separately, extract the entire ZIP, click play, and teleport once while capturing if character identity confirmation is missing. The app remains local-only. Public assets contain blank profiles and no player recordings, learned names or saved window preferences. Official class emblems remain credited to NC Corporation in NOTICE.md.

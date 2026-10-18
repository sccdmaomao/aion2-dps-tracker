Local DPS Tracker 1.0.1 fixes party encounter history and simplifies fight selection.

- Leaving or changing party preserves recorded members’ names, damage, DPS and contribution percentages in the chart, exports and copied chart images. New hits from former members remain excluded.
- One target dropdown now selects both the target and its fight. Repeated fights stay separate, each fight has an All targets option, and Latest fight follows new combat automatically.
- Removed the separate encounter selector and redundant encounter number beside the timer. Status and elapsed time remain visible.

Getting started: install the .NET 8 Desktop Runtime (Windows x64) and Npcap, extract the entire ZIP, open LocalDpsTracker.exe, click play, then teleport once to refresh your character identity and NPC spawn data. Names are detected automatically.

Validation: Windows build and offline self-tests, including party-leave/history/metadata regressions; synthetic UI checks for repeated targets, target filtering, stable history selection during new fights, default/minimum layouts and full-chart image rendering. These checks do not establish independent in-game damage accuracy.

The release includes the licensed English target catalog, blank profiles and no player recordings or learned name caches. Encounters still end after 15 seconds without damage, including that grace period in DPS time. Known protocol, identity/spawn, combat-power, pet and relay limitations remain unchanged.

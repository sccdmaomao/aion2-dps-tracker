Local DPS Tracker 1.2.0 adds official Aion 2 class emblems beside player names.

- Official class emblems appear in full view, compact view and copied damage charts. Hover an icon to see its class name.
- The eight supported class emblems are embedded in the app and work offline; the tracker makes no network requests to load them.
- Class information comes from existing character and party records. Distant party members can retain a class icon before their combat entity arrives. Unknown or conflicting classes remain unavailable.
- Finished encounter rows preserve their captured class information after party changes. Class metadata never establishes party membership or expands capture scope.
- Copied charts include a small artwork credit. Official emblems remain NC Corporation artwork, separate from the application's MIT license; source URLs and hashes are recorded in the repository and credits are included in NOTICE.md.

Getting started: install the .NET 8 Desktop Runtime (Windows x64) and Npcap separately, extract the whole ZIP, click play, and teleport once while capture is running if identity confirmation is missing. Use windowed or borderless mode for the compact desktop overlay.

Validation: clean Windows build and offline self-tests, including class metadata, ambiguous caches, distant members, session reset and preserved encounter metadata. Synthetic full/minimum/compact and copied-chart renders checked all eight emblems and five-player layouts. The official source WebP images and embedded PNGs retain identical RGBA pixels. Existing damage-decoding and accuracy limitations remain unchanged.

Public assets contain blank profiles and no player recordings, learned names or saved window preferences. Previous confirmed-defeat timing, whole-number DPS and chained-hit support remain included.

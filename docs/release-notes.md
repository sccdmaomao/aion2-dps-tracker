Local DPS Tracker 1.1.0 adds a compact combat view.

- A small, resizable window shows every party member’s DPS, the target selector and encounter timer while capture continues.
- Compact view stays on top automatically. Drag the DPS header to move it; left-click the percentage dropdown to select 30–100% opacity. No pin control or right-click menu.
- The restore icon returns to full view and turns always-on-top off. Capture and chart-copy controls work in both views.
- Mode, opacity, both window sizes and position are remembered locally. Windows saved on disconnected monitors are moved into a visible work area.
- Full-view Settings now open with a left click. Encounter exports, extra columns and troubleshooting remain available there.
- Copied charts remain full-size and opaque, including when the app starts directly in compact view.

Getting started: install the .NET 8 Desktop Runtime (Windows x64) and Npcap, extract the entire ZIP, open LocalDpsTracker.exe, click play, then teleport once to refresh character identity and NPC spawn data. Use windowed or borderless game mode for desktop-window visibility; exclusive fullscreen can cover an always-on-top window.

Validation: Windows build and offline self-tests; synthetic five-player UI checks at default/minimum sizes, left-click opacity and Settings popups, native Windows opacity/topmost checks, restore/startup preferences, target selection during new fights, off-screen recovery and opaque full-party chart rendering. No new independent in-game damage benchmark is claimed.

The licensed English target catalog remains bundled. Release assets contain blank profiles and no player recordings, learned name caches or saved window preferences. Known protocol, identity/spawn, combat-power, pet and relay limitations remain unchanged.

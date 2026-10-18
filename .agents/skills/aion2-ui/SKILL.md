---
name: aion2-ui
description: Change the Local DPS Tracker WPF interface or clipboard chart rendering, including compact layout, capture controls, party rows, target selection and right-click options.
---

# Change the meter UI

Read [CONTRIBUTING.md](../../../CONTRIBUTING.md). Main files are
`src/LocalDps/MainWindow.xaml`, `MainWindow.xaml.cs`, `Meter.cs` and
`CombatScope.cs`. Preserve the current layout unless the task asks to change it.

## Current interaction model

- Automatic character detection with teleport guidance; no editable name field.
- Target selector, encounter timer/totals and party chart in one window.
- Play/stop and share icons at the right of the encounter status. Keep tooltips,
  accessible action names and capture-state transitions consistent with the icons.
- No branding banner, persistent footer or settings/options menu in either view.
  Controls use left clicks; capture status is available on capture hover.
- Compact view stays on top, shows party DPS/target/timer and moves by dragging
  the DPS header. Its percentage dropdown selects opacity. Restoring full view
  turns always-on-top off. Window preferences are saved locally.
- Column resizing/reordering is disabled. The player column adapts to window
  width. Names show item level/power; unavailable values use a dash.

UI filtering selects a view; it must not change which party members are captured.
Preserve idle/unmapped party rows and party attribution in `CombatScope`.
Background packet callbacks and UI updates share state through the existing gate
and dispatcher. Keep clipboard work on the WPF STA thread.

## Copying a chart

`CreateChartSnapshot()` builds an independent view with the selected target,
encounter summary and all party rows. Preserve off-screen rows, visible optional
columns, dark background and readable output resolution. Do not resize/scroll
the live grid or capture the desktop/game window to implement chart sharing.

`CopyChartClick()` writes a bitmap to the Windows clipboard and briefly changes
the icon on success/failure. Sharing here means local clipboard copying, not an
upload or external share service.

## Validate the relevant states

Build on Windows. Inspect first launch, detected identity and the changed states
at default and minimum window sizes. For capture controls check both play and
stop, action tooltips and accessible names. For dropdown changes actually open the popup; it is outside the main window's
visual subtree. Check native opacity and topmost state through resize, restart
and restore. WPF content opacity alone does not fade the opaque HWND background;
keep whole-window alpha limited to this app's own HWND.

For image changes, render a representative full party with synthetic data,
including a short viewport and missing stats. Check that the image contains every
row and that the live grid's size, selection and items stay unchanged. An
image-render test does not establish that the Windows clipboard was exercised;
state what was tested accurately.

Use an available UI tool or a temporary WPF render harness. Ignored `work/`
helpers are developer-local and are not dependencies of a fresh checkout. Publish
only blank/synthetic screenshots; `docs/images/startup.png` is the README image.
Cosmetic edits generally need visual/build checks, not new logic-mirroring tests.

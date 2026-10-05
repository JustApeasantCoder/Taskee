# Taskee 0.1 implementation validation

This document records the original 0.1.0 live validation. See
[0.1.1 review fixes](review-fixes-0.1.1.md) for the subsequent corrections,
regression checks, and their current validation limits.

Validated on 2026-10-06 on Windows 11 25H2, build 26200.9550, primary
3440-pixel display at 125% scaling (2752 DIP), left taskbar alignment. The
secondary display has a different width. Explorer PID remained 34124 and
responding throughout the final implementation checks.

## Automated and package checks

- Native Release build and .NET options build: zero warnings/errors.
- 22 metric/rate/profile checks pass: elapsed-time rates and reset detection,
  time-weighted averages with missing intervals, core/package separation,
  duplicate-provider exclusion, stale values, unit/precision conversion,
  stacking/order serialization, bounded font sizes, session peaks while hidden,
  and atomic settings replacement with a backup.
- Self-contained Windows x64 portable build succeeds. The native components
  link their C++ runtime statically; dependency inspection shows Windows system
  DLLs only. The .NET desktop runtime is included in the folder.
- All four options pages render. The app's own page captures are under
  `docs/evidence/ui`. A real Windows UI Automation inspection verified the
  visible portable options app, live values, editable controls and connected
  taskbar status. Label editing and autosave were exercised through its actual
  text control, then the original label was restored.

## Live sensors

The i9-13900K's general CPU temperature and power were read from an already
running MSI Afterburner monitoring shared-memory provider. The RTX 4090's
temperature and absolute watts were read through Libre Hardware Monitor 0.9.6.
Windows supplied processor usage, physical memory and elapsed-time network
counter deltas. Automatic networking selected the Intel I226-V Ethernet adapter;
virtual adapters were excluded from automatic selection and remain selectable.

PawnIO was absent and no driver was installed. Direct CPU package and individual
core readings were therefore **not demonstrated live**. Their selection and
aggregation rules pass controlled metric checks, and unavailable explicit
package/core modes produce a dash with an explanation. The general Afterburner
CPU temperature is never relabeled as a package sensor.

## Native taskbar checks

The live portable integration test added 48 temporary minimized app windows,
then removed them. Settled geometry reported:

| Layout | Reserved width | App/Weather/tray overlaps |
| --- | ---: | --- |
| Paired rows | 259 DIP | 0 / 0 / 0 |
| Single row | 493 DIP | 0 / 0 / 0 |
| Reordered cards, larger text and separators | 307 DIP | 0 / 0 / 0 |
| Enabled again after turning the display off | 260 DIP | 0 / 0 / 0 |

Turning the display off and exiting both restored the exact original taskbar
margin. The native subscription was removed. The saved settings file's SHA-256
was unchanged by the private integration test.

Startup waits for stable Weather geometry before reserving space. The panel
follows the system-tray edge as its width changes. It stays hidden while layout
settles, and a failed check restores spacing rather than leaving overlapping
controls. An inactive diagnostics DLL remains loaded until Explorer exits
naturally; this is deliberate lifetime management.

## Limits

Center alignment, taskbar replacements, equal-width multi-monitor identification,
other Windows builds, driver installation/elevation, native drag delivery and
notification delivery have not been validated. Explorer restart detection and
reconnection are implemented; a forced Explorer restart was not performed.
Taskbar integration uses private Windows elements and may need an update after
a Windows change. Excess cards are clipped at the selected maximum width, and
font size adapts to available taskbar height.

# Stat hover history

Taskbar stats and live-preview values show a five-minute graph after a short
hover delay. The graph stays open while hovering and updates with each snapshot.
It shows the latest live sample, the graph scale, and the lowest/highest live
samples in the window. Averages and peaks remain on the taskbar card; their
graph always shows live samples. Hover cards contain only the metric title,
current value, graph, time/value scales, and low/high values.
Percentage graphs retain five grid lines on a fixed 0–100 scale. Other graphs
use five or six round, evenly spaced tick values in the selected display units,
including Fahrenheit and network-rate conversions. Steady readings have enough
range for distinct labels at the selected precision. The scale expands as soon
as a reading leaves its bounds; it shrinks only after a range at least 35 percent
smaller persists for 30 seconds. Unit, precision and alert changes reset the
retained scale immediately.
Automatic network-unit changes re-express the retained bounds and keep the
same shrink delay when an old traffic spike leaves the window.

The plot is taller, with right-aligned value labels close to the grid. Minute
ticks run from −5m through Now. A small dot marks the latest live reading; paused
or unavailable readings remove that marker. Missing-data gaps remain intact.
Configured warning and critical thresholds appear as subtle dashed lines in
the existing alert palette. Thresholds use the original measurement units,
matching the card's alert comparisons, and their positions follow the graph's
display conversion. Non-percentage scales include both thresholds; thresholds
outside the fixed percentage scale are omitted. Coincident thresholds show
one critical line. These additions do not add pointer inspection or controls.

History uses the existing in-memory metric series. It starts when monitoring
starts, keeps collecting for hidden cards, and starts over when a card selects a
different metric/device/sensor. Restarting the app clears history. Resetting
session peaks preserves graph history. Paused, stale and unavailable samples
break the line; the graph leaves uncollected time blank. Appearance's existing
tooltip preference now controls history graphs as well.

The Explorer snapshot contains at most 240 normalized points per stat. The
downsampler preserves extrema and endpoints in five-second buckets, retains
missing-data gaps, and shows samples after monitoring resumes. Labels use each
card's configured units and precision. Native taskbar removal, hidden layouts,
and disabling tooltips close open graphs.

## Validation

- Native Release build and .NET Release build succeed.
- Core checks cover history timing, averages versus live samples, percentage
  scale, stale/paused gaps, resumption, hidden cards, peak resets, metric changes,
  disabled tooltips, spikes, expiration, constant/single/empty values, non-finite
  readings, Fahrenheit and network units, rounded and retained scales, immediate
  expansion, delayed shrinkage, live markers, configured thresholds, all 32 cards
  with alerts, and JSON roundtrip/size.
- Isolated WPF checks verify stable preview targets/tooltips during refresh,
  updated values, disabling tooltips, separate lines across a gap, empty states,
  single-sample dots, minute marks, tighter label placement, and alert lines.
  Updated captures are under `docs/evidence/history-polish` (Git-ignored).
  The saved profile is unchanged by these checks.
- Actual pointer delivery and popup placement inside Explorer have not been
  exercised with a restarted live taskbar in this change.

## Hover fix and text cleanup

The first native hover implementation positioned its ToolTip but did not assign
an owner through ToolTipService.SetToolTip. Windows rejects IsOpen without an
owner; the old catch handler hid that exception. The tooltip is now registered
to its stat before opening. Open/render failures are logged, and service timeout
closes reopen only while the pointer is still over the stat. Exit, clicks,
layout hiding and teardown clear the hover state before closing.

Thirteen native XAML-island regression checks exercise actual Windows controls in a
private hidden host: reproduce the unowned-tooltip rejection, verify owner
registration, verify the compact graph content, open an owned graph, and close
it, verify five- and six-line scales, minute marks, endpoint markers, dashed
threshold positions/colors, and snapshots without the new fields. This test
does not attach to Explorer or move the pointer.
Null history objects safely use the legacy tooltip. Incomplete tick arrays keep
a valid grid. Failed history UI checks write a failure report and return exit
code 1; installer QA now verifies the installed graph renderer as well.

All four Taskee pages now omit slogans, repeated explanatory paragraphs,
duplicate provider summaries, redundant success messages and taskbar geometry
tooltips. Alert units are part of their field labels. Controls, graph axes,
operational states and actionable errors remain visible. Page captures are
under `docs/evidence/cleanup`.

Run the isolated graph UI check without sensors or taskbar attachment:

```powershell
.\app\Taskee.App\bin\Release\net9.0-windows\Taskee.exe --ui-test --no-taskbar --history-test --tray --capture-dir 'docs\evidence\history'
```

Inspect `history-ui-checks.json` for the UI check result. The rendered examples
use controlled samples, including a brief spike and a missing-data interval.

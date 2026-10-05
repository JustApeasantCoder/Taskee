Taskee 0.1.2

Run Taskee.exe. Keep the whole folder together in a writable location.
Taskee reserves native taskbar space beside Windows Weather or the clock.
Supports Windows 11 taskbars with left alignment. General > Taskbar monitors
can also show the same stats on the second and third monitors independently.
The primary stays enabled; other monitors are ordered left to right, then top
to bottom, which may differ from Windows display numbers. Enable "Show my
taskbar on all displays" in Windows. Disconnected monitors wait for their
taskbars to return. Turning an option off restores that taskbar's spacing.
Other Windows builds and shell customizations may differ.

The options window contains Taskbar, Appearance, Sensors and General pages.
Changes save automatically, with retries if a temporary file lock blocks saving.
Unreadable or newer profiles are protected from replacement. Use Save recovery
profile to preserve copies of the original files before saving current settings.
Drag stat cards or use their arrows to reorder.
Stacking a card puts it below the previous card, up to the selected row limit.
Hover over a taskbar stat or preview value for five minutes of live history,
with current and low/high values. Graphs use the selected units and leave gaps
for unavailable readings. Rounded scales, minute marks, a latest-reading dot
and dashed warning/critical lines make the history easier to read.
Toggle graphs in Appearance. History starts fresh
when the app starts; resetting session peaks keeps the graph history.
Closing the options window keeps monitoring active in the tray by default.
Use Exit Taskee or the tray's Exit action to stop monitoring and restore spacing.
Running Taskee.exe --exit also closes the running instance.

GPU sensors come from Libre Hardware Monitor. CPU readings can come from an
existing MSI Afterburner monitor. Direct CPU package and individual core sensors
require the PawnIO driver and elevated access to the separate sensor helper.
The Sensors page explains access and provides a link to https://pawnio.eu/.
No driver is installed automatically. Missing or stale readings show a dash.
Automatic CPU temperature uses a package sensor when available, then the highest
reported core, then a provider's general CPU temperature. Explicit package mode
never substitutes a general or core reading.

Network rates measure traffic on the selected adapter, including local traffic.
Automatic mode selects a connected physical adapter; all adapters are selectable.
Temperature thresholds use Celsius even when the displayed unit is Fahrenheit.
Other thresholds use W, %, GiB, or bytes/s, as explained in the card editor.

Settings and app logs: %LOCALAPPDATA%\Taskee
Native taskbar sessions: runtime\sessions beside Taskee.exe
The native DLL stays loaded but inactive until Explorer exits naturally.
Windows internals are used; updates to Windows may require a Taskee update.

Open-source component notices and source locations are in THIRD-PARTY.md.

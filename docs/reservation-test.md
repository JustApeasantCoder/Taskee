# Taskbar reservation test — 6 October 2026

## Verified final implementation

Environment: Windows 11 25H2 build 26200.9550; primary taskbar width 2752 DIP,
height 48 DIP, scaling 125%; buttons aligned left. Native taskbar observations
were collected in Explorer through XAML Diagnostics.

Final automatic test session:
`build/sessions/20261006-010807-407b5c45`.

| Check | Observed result |
| --- | --- |
| Native layout reservation | Repeater width reduced from 2752 to 2424 DIP; right inset 328 DIP held |
| Stats panel | x=1880.8, width=320 DIP; right edge=2200.8 |
| Native Weather, normal load | x=1720.8, width=152 DIP; 8-DIP gap before stats |
| Native Weather, crowded | x=1828.8, width=44 DIP; Windows compacted it and preserved the gap |
| System tray | x=2212.8; 12-DIP clearance after stats |
| 48 additional temporary windows | 30 app buttons visible in the native repeater; zero stats overlap |
| Windows overflow | Native `Taskbar.OverflowToggleButton`, accessibility name `Taskbar overflow menu`, visible |
| Removal | Original margin restored exactly; full repeater width restored |
| Explorer stability | PID 34124 remained unchanged and responsive during the final test |

The controller created 48 minimized windows with separate taskbar identities at
about six seconds, cleared them at 23 seconds, then exited at 30 seconds.
Both normal and crowded samples showed zero app, Weather and tray overlap once
Windows' layout animation settled. The panel is transparent during that animation.

This is live native layout/overflow evidence. Sensor numbers are simulated.
No claim is made about real CPU/GPU temperature, power, network speed, click
behavior in Windows' overflow menu, or compatibility with other taskbar layouts.

## Discarded experiment

Directly expanding the native Weather control's width caused Explorer to
restart at 00:53:32 on this PC. Windows recorded Event 1000 in `Taskbar.View.dll`,
exception `0xc0000409`, offset `0x4402c`. That modification was removed.
The final implementation changes the app layout's margin and adds its own panel;
it does not resize or modify Weather's content.

All test windows were removed. The final automatic test restored the taskbar,
unsubscribed XAML Diagnostics and left Explorer responsive.

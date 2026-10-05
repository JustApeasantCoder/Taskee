# Additional taskbar monitors

Implemented on 2026-10-06. General → Taskbar monitors has independent options
for the second and third monitors. Both default off, including when loading
existing profiles. The primary stays selected while the global taskbar toggle
is on. Import/export and automatic saves retain both choices.

The native renderer identifies taskbars through their XAML host windows and maintains
separate bindings, timers, validation, original margins, and status files for
each taskbar window. Secondary taskbars can anchor beside the clock without a
Weather control. Disabling an individual monitor restores only its margin.
Frame removal invalidates queued discovery, and loaded/size changes retry
discovery after initial layout. Additional monitors are ordered left to right,
then top to bottom; Windows must show a taskbar on those displays.

Validation completed:

- Native and managed Release builds passed with no warnings or errors.
- Native policy checks passed for independent selections and isolated binding
  removal/reconnection. The host-discovery fix below adds ancestry checks.
- Managed checks passed, including legacy-profile defaults, selection
  serialization, snapshot delivery, independent third-monitor selection, and
  the global off toggle retaining monitor choices. The final combined build
  also includes the concurrent history-tooltip work; all 65 managed checks pass.
- A self-contained build is staged at `dist/Taskee-multi-monitor/Taskee.exe`.
- An isolated `--ui-test --no-taskbar` run exited successfully and rendered all
  four pages. The General capture was inspected: both options are visible and
  readable. Captures are retained under `docs/evidence/multi-monitor` (ignored
  by Git). The user's saved settings hash was unchanged.

The installed Taskee and Explorer processes were left running. Updated native
rendering on a second/third display, hotplug, and per-monitor restoration have
not been demonstrated live in this initial pass. The staged build was not installed.

## Fix for taskbar discovery waiting indefinitely

The `Taskee-multi-monitor` build attached to Explorer successfully but never
created a per-window status file. A read-only XAML Diagnostics probe reproduced
the cause: `GetBoundingRectangle()` inside the Explorer XAML island returned
`0,0,3440,60` for the primary frame and `0,0,2560,60` for the secondary frame.
The actual taskbar screen rectangles were `0,1380,3440,60` and
`460,-60,2560,60`. Comparing the island-local origin with screen coordinates
rejected both taskbars. Creating the control's actual automation peer returned
the same island-local origin, so changing peer creation does not fix it.

Discovery now follows diagnostics visual ancestry to the
`DesktopWindowXamlSource`, reads its native child HWND, and validates the
top-level ancestor as an Explorer taskbar. The probe resolved both host windows
correctly without changing taskbar margins. Source/ancestor removal also
invalidates the associated reservation. The options app uses the initial native
status while a per-window status is still unavailable.

The corrected self-contained build is at `dist/Taskee-taskbar-fix/Taskee.exe`.
Release builds pass with no warnings or errors; all 29 native policy checks and
67 managed checks pass. Native checks include independent host ancestry,
removed hosts, reparented controls, missing parents, and cycle rejection.

With user approval, Taskee was restarted with the corrected build. Live native
checks on both taskbars passed at a requested width of 365 DIP with zero app,
Weather, and tray overlaps. A temporary snapshot enabled both displays,
disabled only the secondary, then enabled it again. The primary remained
connected throughout; secondary removal and final shutdown restored exact
original margins (`exactMargin=1`). Evidence is retained under the ignored
`build/taskbar-live-check-4c56ccea9d7148209d795be18cacd8e6` folder.

The normal corrected app was resumed after the fixture check. The user's saved
settings SHA-256 stayed unchanged, and Explorer retained PID 34124. The saved
profile selects the primary only; enabling the secondary for the check did not
persist a settings change. Third-monitor rendering, hotplug, and actual control
recreation remain untested.

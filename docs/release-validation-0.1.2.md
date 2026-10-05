# Taskee 0.1.2 release validation

Validated on Windows 11 x64 on 2026-10-06.

## Final review fixes

- Automatic network-unit changes retain the graph's raw bounds and honor the
  full 30-second shrink delay after a traffic spike expires.
- A native snapshot with `history: null` uses its legacy text tooltip safely.
  Incomplete WPF tick arrays retain a valid grid.
- Failed graph UI checks return exit code 1, including when their failure report
  cannot be written. Installer QA checks the installed graph renderer.
- Build and launch defaults follow the application version. The settings window
  reports its assembly version instead of a hardcoded label.

## Automated and packaged checks

- Native and managed Release builds succeed with zero warnings or errors.
- Managed core: 86 checks pass.
- Native layout and host policy: 29 checks pass.
- Native tooltip: 13 checks pass in a private hidden XAML host. The checks cover
  actual tooltip ownership/opening, renderer geometry, thresholds, and
  compatibility with older or null graph payloads.
- WPF history: 14 checks pass, including retained hover targets, graph refresh,
  five-line axes, minute labels, markers, thresholds, and empty states.
- An intentionally invalid capture directory returns exit code 1.
- The self-contained portable ZIP contains 494 files under one `Taskee` root.
  It excludes saved settings, runtime data, source files, debug symbols, and logs.
- Installer, same-version repair, and uninstall pass. All 494 installed payload
  files match the portable package by SHA-256. Both Start Menu shortcuts are
  created and removed, and uninstall registration is removed.
- All four installed settings pages and the graph examples render successfully.
  Captures were inspected for layout, labels, and graph placement.
- Saved settings, backup settings, and the Windows startup entry remain
  unchanged. Explorer process identities remain unchanged.

Build logs, captures, and installer results are retained under the Git-ignored
`build` directory. Release downloads include the installer, portable ZIP, and
a SHA-256 checksum manifest.

## Live validation boundaries

This release review uses isolated UI and installer checks and does not replace
or restart the user's live taskbar app. The user confirmed working native hover
earlier in the graph work. Separate multi-monitor validation records a live
primary/secondary taskbar check with exact margin restoration and unchanged
saved settings; see [multi-monitor validation](multi-monitor-validation.md).
Third-monitor rendering, hotplug, and actual taskbar-control recreation remain
untested live.

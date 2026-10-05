# Taskee 0.1.1 review fixes

Implemented and validated on 2026-10-06.

| Review issue | Result |
| --- | --- |
| Settings overwritten after a failed load | Startup no longer saves a successfully loaded profile unnecessarily. Unreadable, recovered, or unsupported profiles are protected from automatic saving. A persistent recovery notice offers an explicit recovery action that archives both original files before replacement. A newer primary profile is never replaced by an older backup automatically. |
| Five-second refresh displays intermittent dashes | Freshness accounts for the selected and producer refresh intervals plus measured collection time. Missing or genuinely stale data still displays a dash. |
| Reversed native width bounds | Available taskbar space takes precedence over a configured minimum. Bounds remain ordered, rounding cannot exceed available space, and a slot with no room produces no panel. |
| Replaced taskbar controls cannot reconnect | Weather, frame, root, repeater, and tray removals invalidate the current attachment. Old timers and handles are released. Versioned callbacks prevent stale dispatcher work from reattaching removed controls; settled-tree discovery handles either Add/Remove notification order. |
| Resizing prevents failed-layout cleanup | Validation timing is independent of panel width changes. A continually invalid layout still reaches its restoration timeout. Invisible panels cannot intercept native taskbar clicks. |
| Failed autosaves receive no retry | Pending edits remain queued with bounded retry delays after failed writes. The helper's settings reads permit atomic replacement. |
| CPU temperature accepts unrelated sensors | Both the picker and resolver require CPU temperature sensors, respect an explicit CPU device, and exclude TjMax distance. The generic hardware-sensor card retains access to other readings. |

## Verification

- Native and managed Release builds complete with no warnings or errors.
- 42 managed checks pass, including actual temporary-file lock/retry recovery,
  unsupported-profile preservation across repeated startup attempts, archived
  originals, backup recovery, interval-aware freshness, and CPU sensor filtering.
- 18 native policy checks pass, including narrow/fractional width bounds,
  sustained invalid layouts, control replacement, late removals, cancelled
  attachment callbacks, retry after failure, and owner shutdown.
- The self-contained Windows x64 portable build succeeds.
- Two isolated runs of the portable options app render all four pages and exit
  successfully: normal settings and a protected unsupported-profile fixture.
  Eight captures and `ui-validation.json` are retained locally under
  `docs/evidence/review-fixes` (excluded from Git). The protected fixture's hash
  is unchanged.
- The user's live settings hash is unchanged. Existing Taskee and Explorer
  processes were left running; Explorer remains responsive.

Native policy checks exercise the same policy code used by the Explorer
component. Live Windows control recreation and updated native attachment have
not been demonstrated in this pass. The original build remains running until
the user chooses to apply the update; the previous live taskbar validation is
recorded separately in `implementation-validation.md`.

## Apply the update

Exit Taskee, extract `Taskee-0.1.1-win-x64.zip` into the existing portable
location, replace its `Taskee` app files, and reopen `Taskee.exe`. The ZIP uses a
`Taskee` root folder and excludes runtime session data. Updating the existing
folder preserves the path already selected for Windows startup. User settings
remain in `%LOCALAPPDATA%/Taskee`.

The separately staged executable is also available at
`dist/Taskee-0.1.1/Taskee.exe`. If switching permanently to that folder, toggle
Windows startup off/on in General to update its existing shortcut path.

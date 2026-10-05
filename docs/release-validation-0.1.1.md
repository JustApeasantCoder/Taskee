# Taskee 0.1.1 release validation

Validated on 2026-10-06 on Windows 11 x64.

- Native and managed Release builds pass with zero warnings or errors.
- All 42 managed and 18 native policy checks pass.
- The self-contained portable ZIP contains 494 files under its `Taskee` root,
  including the MIT license and third-party notices, with no runtime sessions.
- The per-user installer installs successfully in an isolated folder. All 494
  payload files match the build output's SHA-256 hashes. Both Start Menu
  shortcuts are present.
- The installed options app renders Taskbar, Appearance, Sensors and General
  pages and exits successfully in isolated UI mode. All four captures were
  visually inspected.
- Reinstalling the same release succeeds. Silent uninstall removes the installed
  executable, Start Menu group and uninstaller registration without a restart.
- Saved settings, the profile backup and the existing Windows startup command
  are unchanged. Explorer retains its process identity and remains responsive.
- Installer logs, page captures and a machine-readable result are retained
  locally under `build/installer-qa`, excluded from Git.

The installer and app are unsigned. Direct CPU package/core access requires a
separate PawnIO installation and elevated sensor helper. No driver is bundled.
The supported taskbar configuration is the primary Windows 11 taskbar, aligned
left, with Widgets enabled. Current installer/UI proof does not demonstrate
updated native attachment or Windows control recreation live; previous taskbar
and crowding validation is documented in `implementation-validation.md`.

Uninstall preserves `%LOCALAPPDATA%/Taskee`. Native session copies beside the
app can remain because Explorer retains an inactive DLL until it exits naturally.
The installer never requests an Explorer restart.

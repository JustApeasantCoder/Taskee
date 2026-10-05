# Taskee

A native Windows taskbar monitor with a configurable WPF options app. CPU/GPU temperature and watts, live download/upload rates, usage, memory and individual hardware sensors can be arranged beside Windows Weather.

## Run

Run `dist/Taskee-0.1.1/Taskee.exe`, or use `Start-Taskee.ps1`. Keep the portable folder together in a writable location. Exit an older running Taskee instance before opening the updated build. The current build supports Windows 11's primary, left-aligned horizontal taskbar with Widgets enabled. Settings save automatically. Closing options keeps the app in the tray by default; **Exit Taskee** restores the original taskbar spacing. `Start-Taskee.ps1 -Stop` closes the running instance.

Failed saves retry automatically. If a profile cannot be loaded, or uses a newer settings version, Taskee protects the original files and displays a recovery notice. **Save recovery profile** copies both originals into `%LOCALAPPDATA%/Taskee/recovery` before saving the settings currently shown in options. Loading an older backup never silently replaces a newer profile.

## Options

- **Taskbar:** add, duplicate, hide or remove cards; drag or use arrows to reorder; stack individual cards below the previous card. Pick a device/adapter or specific hardware sensor, units, precision and a short label.
- **Temperature:** automatic selection, explicit package temperature, highest reported core, average of reported core sensors, or a specific sensor. Package selection never silently substitutes another reading.
- **Time:** current values, a time-weighted rolling average, rolling peak/minimum (2–600 seconds), or a resettable session peak. Missing/stale values display a dash; missing intervals are excluded from averages.
- **Appearance:** installed fonts, weight and size; 1–3 stacked rows; column/row spacing, padding, width limits, stable widths, separators, opacity, background color and radius. Four presets provide starting layouts.
- **Alerts:** per-card warning/critical thresholds, shared alert colors, optional critical notifications. Thresholds use underlying units (°C, W, %, GiB, bytes/s).
- **Sensors:** live sensor inventory and provider details, a read-only MSI Afterburner bridge, sensor-helper restart and optional elevated CPU access.
- **General:** refresh interval, close-to-tray, optional Windows startup, profile import/export and session-peak reset. Startup is off unless enabled.

The preview uses live readings. Double-clicking the taskbar stats opens options. Network rates measure adapter traffic, including local transfers; they are not an internet speed test. Automatic adapter selection chooses a connected physical adapter; VPN/virtual adapters are available for explicit selection.

## CPU sensor access

GPU telemetry uses [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). CPU telemetry can use a running MSI Afterburner monitor. Direct package and core sensors require the [PawnIO driver](https://pawnio.eu/) and an elevated sensor helper. The Sensors page explains this and offers an elevation button once the driver is installed. Taskee does not install drivers or change security settings. The options app and Explorer component remain unelevated.

On the development PC, real CPU temperature/power are available through the existing Afterburner monitor, GPU temperature/power through Libre Hardware Monitor, and network rates through Windows. The Afterburner CPU temperature is a general provider reading; package and individual core modes need direct access.

## Build and checks

Requires Visual Studio 2022 C++ Build Tools, Windows SDK, CMake and .NET 9 SDK:

```powershell
.\Build.ps1 -Test -Portable
.\Start-Taskee.ps1
```

`-Portable` builds a self-contained Windows x64 folder; no .NET installation is needed to run it. `-OutputDirectory` selects its destination. `-Test` runs the native taskbar policy and metric/rate/profile checks. The original reservation experiment remains available through `Start-Test.ps1`; do not run it at the same time as the full app.

Additional development checks:

```powershell
dotnet run --project .\app\Taskee.Tests -c Release -- --probe
.\app\Taskee.App\bin\Release\net9.0-windows\Taskee.exe --ui-test --no-taskbar
.\app\Taskee.App\bin\Release\net9.0-windows\Taskee.exe --integration-test
```

UI/integration modes use temporary defaults and leave the saved profile unchanged. Integration mode uses real sensors and tests layout changes, display off/on and cleanup. `build/Release/TaskeeLoadTest.exe 13` adds 48 temporary minimized windows for crowding validation, then removes them. `--capture-dir` exports the app's own rendered pages for layout review.

## Implementation

- `app/Taskee.Core`: validated settings, sensor providers, rates, metric selection, bounded history and formatting.
- `app/Taskee.App`: native options UI, tray, current-user-only named-pipe sensor helper, atomic snapshots and taskbar reconnection.
- `src/panel.cpp`: a small XAML Diagnostics component inside Explorer. It renders supplied text and reserves native taskbar space; it performs no hardware access.
- `src/bridge.cpp`: native diagnostics connection and owner lifetime.

Taskee inserts its own XAML panel and insets the app-button layout. Windows manages its Weather control and app overflow. It hides the panel during layout movement, validates overlap, and restores spacing if validation fails or the owner/data disappears. An inactive diagnostics DLL remains loaded until Explorer exits naturally. Each launch uses a unique DLL copy under `runtime/sessions` beside the app. Settings and app logs live in `%LOCALAPPDATA%/Taskee`; settings use atomic replacement with a backup.

Removed taskbar controls invalidate pending callbacks, release the old inset, and allow replacement controls to reconnect. Native width limits give available space precedence over an oversized minimum. Failed-layout timing continues across value-driven resizing, and hidden panels do not intercept clicks.

Private taskbar elements are used; future Windows changes may require an update. Center alignment, taskbar replacements, equal-width multi-monitor identification, and other Windows builds have not been validated. Width limits clip excess cards; choose fewer columns or stacked rows for a smaller footprint. Font size is bounded by the available taskbar height.

See [implementation validation](docs/implementation-validation.md), the original [reservation test](docs/reservation-test.md), and [component notices](THIRD-PARTY.md).

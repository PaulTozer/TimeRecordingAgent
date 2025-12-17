# Architecture Overview (C#)

## Solution Layout
```
TimeRecordingAgent.sln
├── src
│   ├── TimeRecordingAgent.Core           # Window polling, parsing, storage, shared models
│   │   ├── Models/
│   │   ├── Services/
│   │   └── Storage/
│   └── TimeRecordingAgent.App            # WPF tray host + presentation
│       ├── App.xaml
│       ├── Tray/
│       └── Views/
└── tests
    └── TimeRecordingAgent.Core.Tests     # xUnit tests for parsers/session coalescing
```

## Runtime Flow
1. `App.xaml.cs` boots without showing a window, initializes dependency graph, and spins up `TrayIconManager`.
2. `TrayIconManager` owns a `RecordingCoordinator`, exposes Start/Pause/Export actions, and keeps the tray tooltip in sync with the last recorded document.
3. `RecordingCoordinator` wires together:
   - `ScreenStateMonitor` (detects screensaver, session lock, suspend/resume events via Win32 + `SystemEvents`).
   - `ForegroundWindowPoller` (polls `GetForegroundWindow` every N seconds, resolves process + window title, and finalizes `ActivitySample` segments when the focused document changes).
   - `OutlookContextReader` (optional) to fetch the subject of the active mail inspector/explorer.
   - `SqliteTimeStore` for persistence and aggregation queries.
4. Each finalized sample is normalized by `WindowContextResolver`, which removes boilerplate suffixes (" - Word", " - Outlook") and merges duplicate samples shorter than two seconds.
5. The store writes to `activity_log`, and the coordinator raises events so the UI can refresh tooltips or show toast notifications.

## Key Components
- **ForegroundWindowPoller** — uses Win32 `GetForegroundWindow`, `GetWindowTextW`, and `GetWindowThreadProcessId` plus `Process` APIs to capture the active context. It is timer driven (System.Threading.Timer) and coalesces contiguous focus spans.
- **ScreenStateMonitor** — wraps `SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)` and `SystemEvents.PowerModeChanged/SessionSwitch` to expose `IsScreenUnavailable` in real time. When the system suspends or locks, the poller is paused and the current sample flushed.
- **OutlookContextReader** — leverages `Microsoft.Office.Interop.Outlook.Application` to inspect `ActiveInspector().CurrentItem` or the selected item in `ActiveExplorer()`. It degrades gracefully if Outlook is not installed or automation is disabled.
- **SqliteTimeStore** — ensures schema on startup, writes log rows inside a lightweight connection pool, and exposes `GetDailySummary(DateOnly date)` returning aggregated durations per process/document.
- **TrayIconManager** — WinForms `NotifyIcon` embedded in WPF app; hosts context menu (Start, Pause, Export Today, Open Folder, Exit) and surfaces balloon tips on export/completion.

## Threading & Safety
- Foreground polling runs on a background timer thread; callbacks marshal to the thread pool when persisting to SQLite to avoid blocking the Win32 timer callback.
- WPF/Tray updates are dispatched onto the UI thread via `Application.Current.Dispatcher`.
- SQLite access is serialized through the `SqliteTimeStore` because writes are simple and infrequent (a few per minute).

## Data Model
```
CREATE TABLE activity_log (
    id INTEGER PRIMARY KEY,
    started_at TEXT NOT NULL,
    ended_at TEXT NOT NULL,
    process_name TEXT NOT NULL,
    document_name TEXT NOT NULL,
    window_title TEXT NOT NULL
);
CREATE INDEX idx_activity_log_started_at ON activity_log(started_at);
```

## Extensibility Hooks
- Future integrations (e.g., export to Harvest) can subscribe to `RecordingCoordinator.SampleStored`.
- Additional application-specific resolvers can be registered with `WindowContextResolver` via dictionary lookups keyed by process name.

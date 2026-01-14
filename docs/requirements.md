# Time Recording Agent — Requirements (C# Edition)

## Problem Statement
Consultants frequently forget to record the exact document or email they were focused on while jumping between Word, Outlook, and other desktop apps. Manual timers are imprecise, and back-filling time after a long day leads to inaccurate billing. We need a lightweight Windows tray application that continuously records which document or email is active, but only when the device is truly in use.

## Success Criteria
- Windows tray app written in C#/.NET 10 that starts with the OS and provides pause/resume/exit controls.
- Captures the foreground window title, executable name, and precise timestamps at a configurable cadence (default 5 seconds).
- Extracts the meaningful document identifier from titles (e.g., `ProjectPlan.docx` for Word or the current message subject for Outlook).
- Skips logging whenever the screensaver is running, the workstation is locked, or the device is in standby/suspend mode.
- Persists observations locally in SQLite with no external dependencies or AI services.
- Exposes a simple daily summary (CSV export or on-disk JSON) so the user can reconcile time sheets quickly.

## Functional Requirements
1. **Foreground Monitoring**
   - Poll the active window handle via Win32 (`GetForegroundWindow`, `GetWindowText`) and map it back to the owning process.
   - Coalesce sequential samples of the same document into a single segment to avoid row explosion.
   - Provide tray menu actions to instantly pause/resume monitoring.
2. **Document Context Extraction**
   - Word/Excel/PowerPoint: infer document name by splitting the window title before ` - Word`, ` - Excel`, etc.
   - Outlook: use `Microsoft.Office.Interop.Outlook` to query the active inspector or explorer and capture the current message subject/folder rather than relying solely on the window caption.
   - Generic fallback uses the raw window title.
3. **Recording Rules**
   - Respect the screen state: if the screensaver is running, the workstation is locked, or `PowerModeChanged` indicates `Suspend`, immediately flush/stop tracking.
   - Automatically resume after `PowerModeChanged: Resume`.
   - Avoid writing duplicates shorter than 2 seconds to reduce noise.
4. **Storage**
   - SQLite database stored next to the executable (`data/time-tracking.db`).
   - Table `activity_log(id, started_at, ended_at, process_name, document_name, window_title)` plus indexes on `started_at`.
   - Provide helper queries for "today" grouped durations per document.
5. **User Feedback**
   - Tray tooltip showing the document currently being recorded.
   - Context menu item "Open Log Folder" to reveal the data directory.
   - Optional CSV export command for the current day.

## Non-Functional Requirements
- .NET 10.0 (Windows) with nullable reference types enabled.
- No third-party components beyond NuGet packages (`Microsoft.Data.Sqlite`, `Microsoft.Office.Interop.Outlook`).
- CPU usage under 3% on average; memory footprint under 150 MB.
- Unit tests for title parsing, Outlook context extraction (mocked), and session coalescing logic.

## Open Questions / Risks
- Access to Outlook COM automation requires Outlook desktop installed; we need graceful degradation if not available.
- Some applications obfuscate titles (e.g., MDI shells) — additional heuristics may be required later.
- Running under least privileges may block screensaver detection on hardened endpoints; ensure fallbacks exist.

## Next Steps
1. Scaffold .NET solution with core library, WPF tray host, and tests.
2. Implement screen-state monitor + window polling service and wire them through the tray host.
3. Add SQLite persistence, daily summary export, and smoke tests.
4. Package via self-contained installer (tracked separately).

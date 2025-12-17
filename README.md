# Time Recording Agent (.NET)

A lightweight Windows tray application that records which document or email you are working on by inspecting the active window title. Built with C#/.NET 8, it stores events locally in SQLite and avoids recording when the computer is idle, locked, or asleep.

## Features
- System tray (NotifyIcon) host with start/pause/export actions.
- Foreground window polling every 5 seconds using Win32 APIs.
- Document detection for Word/Excel/PowerPoint and Outlook mail subjects; generic fallback for other apps.
- Screensaver/standby detection so idle time is ignored automatically.
- SQLite-backed log with helper query for "today" aggregated durations.
- Left-click history window to review entries, approve/delete them, and group related work items.

## Prerequisites
- Windows 10/11 with .NET 8 SDK installed.
- Outlook desktop (optional) if you want active mail subject logging.

## Getting Started
1. Restore tools: `dotnet tool restore` (if we add local tools later).
2. Restore packages and build: `dotnet build TimeRecordingAgent.sln`.
3. Run the tray app: `dotnet run --project src/TimeRecordingAgent.App/TimeRecordingAgent.App.csproj`.
4. Left-click the tray icon to open the history window where you can approve, delete, or group entries. Right-click for the classic menu to pause or export CSV; the first run creates `data/time-tracking.db` automatically.

## Project Structure
- `src/TimeRecordingAgent.Core` — models, window polling, screen-state monitor, SQLite storage.
- `src/TimeRecordingAgent.App` — WPF host, tray UX, wiring.
- `tests/TimeRecordingAgent.Core.Tests` — xUnit tests for parsers and session grouping.

## History Window
- Left-click the tray icon to open the review window.
- Multi-select rows to approve or mark them pending, delete mistakes, or assign a group label.
- Use the text box to type a group name and click *Assign Group*; click *Clear Group* to remove it.
- Toggle *Show pending only* to focus on unapproved entries. Group totals at the bottom update live so you can see how much time each group represents.

## Roadmap
- Optional UI for viewing/editing historical entries.
- Installer packaging (MSIX or Squirrel).
- Additional application-specific parsers (Teams, browsers, IDEs).

# Time Recording Agent (.NET)

A lightweight Windows tray application that records which document or email you are working on by inspecting the active window title. Built with C#/.NET 10, it stores events locally in SQLite and avoids recording when the computer is idle, locked, or asleep.

## Features
- System tray (NotifyIcon) host with start/pause/export actions.
- Foreground window polling every 5 seconds using Win32 APIs.
- Document detection for Word/Excel/PowerPoint, Outlook mail subjects, and browser tabs; generic fallback for other apps.
- Browser title normalization to aggregate time correctly across tab switches.
- Screensaver/standby detection so idle time is ignored automatically.
- SQLite-backed log with helper query for "today" aggregated durations.
- Left-click history window to review entries, approve/delete them, and group related work items.
- **AI-Powered Task Classification** — Automatically detects customer names and suggests billing categories using GitHub-hosted AI models.
- **Vision-based Context** — Uses screen capture for apps where content can't be extracted directly (Teams, browsers, etc.).
- **Description Field** — AI can suggest descriptions based on email/document body content.

## AI-Powered Suggestions

The application includes an AI classification feature that analyzes your work context (window title, document name, and process) to:
- **Detect customer/client names** from document names, project folders, or window titles
- **Suggest work categories** (Development, Support, Meeting, etc.)
- **Recommend billing status** based on the nature of the work

### Configuring AI Suggestions

AI suggestions require a Microsoft Foundry (Azure AI Foundry) deployment:

1. Deploy a model in [Microsoft Foundry](https://ai.azure.com) (e.g., `gpt-4o-mini`)
2. Right-click the tray icon and select **"Configure AI..."**
3. Enter your settings:
   - **Endpoint URL**: Your Microsoft Foundry endpoint (e.g., `https://your-resource.openai.azure.com/`)
   - **API Key**: Your API key from the Foundry deployment
   - **Model**: The deployment name (e.g., `gpt-4o-mini`)
4. Click **"Test Connection"** to verify the settings work
5. Click **"Save"** to store the configuration

Settings are saved to `data/settings.json` in the application directory.

### How It Works

When you've been working on a task for 5+ minutes, a classification dialog appears. If AI is enabled:
- The dialog will show pre-filled suggestions based on AI analysis
- A blue banner displays the AI's reasoning for its suggestions
- You can accept, modify, or ignore the suggestions

### Tray Menu Options
- **AI Suggestions Enabled**: Toggle AI classification on/off
- **Configure AI...**: Open the settings dialog to configure Microsoft Foundry connection
- **Task Prompts Enabled**: Toggle the 5-minute classification prompts

## Prerequisites
- Windows 10/11 with .NET 10 SDK installed.
- Outlook desktop (optional) if you want active mail subject logging.
- Microsoft Foundry deployment (optional) for AI-powered suggestions.

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

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

## Timesheet Verification with Microsoft Foundry Agent

The application can automatically verify your daily timesheet entries against outside counsel guidelines using Microsoft Foundry (Azure AI Foundry). This helps ensure compliance before submitting timesheets.

### What It Checks

The verification agent analyzes each time entry for:
- **Vague descriptions** — Flags entries like "work on matter" or "review documents"
- **Block billing** — Detects multiple tasks combined in one entry
- **Excessive time** — Questions unusually large time increments
- **Category mismatches** — Verifies category matches the description
- **Billability issues** — Identifies administrative tasks marked as billable
- **Missing information** — Ensures required fields are populated

### Setting Up Microsoft Foundry Agent

#### Prerequisites

1. **Azure Subscription** — You need an Azure account
2. **Azure CLI** — Install from https://docs.microsoft.com/en-us/cli/azure/install-azure-cli
3. **Login to Azure** — Run `az login` in your terminal before using the app

#### Step 1: Create an Azure OpenAI Resource

1. Go to the [Azure Portal](https://portal.azure.com)
2. Search for **"Azure OpenAI"** and click **Create**
3. Fill in:
   - **Subscription**: Your Azure subscription
   - **Resource group**: Create new or use existing
   - **Region**: Choose a region that supports GPT-4o (e.g., East US, West Europe)
   - **Name**: A unique name (e.g., `mycompany-openai`)
   - **Pricing tier**: Standard S0
4. Click **Review + Create**, then **Create**
5. Wait for deployment to complete

#### Step 2: Deploy a Model

1. Once the resource is created, go to **Azure AI Studio** (https://ai.azure.com)
2. Select your resource/project
3. Go to **Deployments** → **Create deployment**
4. Choose a model:
   - **gpt-4o-mini** — Recommended for cost-efficiency
   - **gpt-4o** — Better accuracy, higher cost
5. Give it a deployment name (e.g., `gpt-4o-mini`)
6. Click **Create**

#### Step 3: Get Your Endpoint URL

1. In Azure AI Studio, go to your project
2. Click on **Overview** or **Endpoints**
3. Copy the **Endpoint URL** — it looks like:
   ```
   https://your-resource-name.openai.azure.com/
   ```

#### Step 4: Configure the Application

Edit your `data/settings.json` file and add the `foundryAgent` section:

```json
{
  "azureAi": {
    "endpoint": "https://your-resource.openai.azure.com/",
    "apiKey": "your-api-key-here",
    "model": "gpt-4o-mini",
    "enabled": true
  },
  "foundryAgent": {
    "projectEndpoint": "https://your-resource.openai.azure.com/",
    "deploymentName": "gpt-4o-mini",
    "enabled": true,
    "outsideCounselGuidelines": null
  },
  "general": {
    "taskPromptsEnabled": true,
    "promptThresholdMinutes": 5
  }
}
```

**Important**: The Foundry Agent uses **Azure CLI authentication** (not API keys). Make sure you've run `az login` before starting the application.

#### Step 5: Verify Setup

1. Run `az login` in your terminal
2. Start the Time Recording Agent
3. The verification feature will now be available

### Custom Outside Counsel Guidelines

You can provide your own guidelines by setting `outsideCounselGuidelines` in the settings:

```json
{
  "foundryAgent": {
    "projectEndpoint": "https://your-resource.openai.azure.com/",
    "deploymentName": "gpt-4o-mini",
    "enabled": true,
    "outsideCounselGuidelines": "Your firm's specific billing guidelines here..."
  }
}
```

If left as `null`, the application uses built-in default guidelines covering:
- Description requirements (specific, detailed entries)
- Block billing prohibition
- Time increment guidelines (0.1 hour minimum, 8 hour maximum)
- Billable vs non-billable classification
- Category accuracy requirements

### Troubleshooting

| Issue | Solution |
|-------|----------|
| "Not configured" | Check that `projectEndpoint` is set in settings.json |
| Authentication failed | Run `az login` and ensure you have access to the Azure OpenAI resource |
| Model not found | Verify `deploymentName` matches your deployed model name exactly |
| Slow responses | Consider using `gpt-4o-mini` instead of `gpt-4o` for faster responses |


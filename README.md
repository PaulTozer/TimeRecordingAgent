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

Create a **Copilot-like agent** in Microsoft Foundry that your team can chat with to verify timesheet entries against outside counsel guidelines — no coding required!

### What the Agent Does

Users paste their timesheet entries into the chat, and the agent analyzes them for:
- **Vague descriptions** — Flags entries like "work on matter" or "review documents"
- **Block billing** — Detects multiple tasks combined in one entry
- **Excessive time** — Questions unusually large time increments
- **Category mismatches** — Verifies category matches the description
- **Billability issues** — Identifies administrative tasks marked as billable

### Step-by-Step: Create Your Timesheet Verification Agent

#### Step 1: Go to Microsoft Foundry Portal

1. Open [https://ai.azure.com](https://ai.azure.com)
2. Sign in with your Azure account
3. Click **"Create an agent"** on the home page

   > If you don't have a project yet, the portal will guide you through creating one.

#### Step 2: Configure the Agent

Once in the **Agent Playground**, configure your agent:

1. **Name**: `Timesheet Compliance Checker`

2. **Instructions** — Copy and paste this into the Instructions field:

```
You are a legal billing compliance assistant that reviews timesheet entries against outside counsel guidelines.

When a user provides timesheet entries, analyze each entry for compliance issues and provide clear feedback.

## GUIDELINES TO CHECK:

### Description Requirements
- Each entry must have a specific, detailed description (minimum 10 words for entries over 0.5 hours)
- Reject vague descriptions like "work on matter", "review documents", "research", "correspondence"
- Good example: "Drafted motion to compel discovery responses regarding defendant's financial records"
- Bad example: "Drafted motion"

### Block Billing (PROHIBITED)
- Each task must be a separate entry
- Flag entries that combine multiple tasks with semicolons or "and"
- Bad example: "Reviewed documents; drafted response; called client" — should be 3 separate entries

### Time Increments
- Minimum: 0.1 hours (6 minutes)
- Maximum single entry: 8 hours (flag anything longer)
- Time should be proportionate to the task

### Billable vs Non-Billable
- Administrative tasks (scheduling, filing, organizing) = NON-BILLABLE
- Internal meetings without client = NON-BILLABLE  
- Training on client matters = may be billable
- Travel: first hour typically NON-BILLABLE

### Prohibited Billing
- Correcting your own errors = NON-BILLABLE
- Staffing decisions = NON-BILLABLE
- Administrative overhead = NON-BILLABLE

## YOUR RESPONSE FORMAT:

For each entry, provide:
1. ✅ COMPLIANT or ⚠️ NEEDS REVIEW or ❌ NON-COMPLIANT
2. Issues found (if any)
3. Specific suggestions to fix

End with a summary showing:
- Total entries reviewed
- Compliant / Needs Review / Non-Compliant counts
- Total billable hours
- Total non-billable hours
```

3. **Model**: Select `gpt-4o` or `gpt-4o-mini` (mini is faster and cheaper)

4. Click **Save**

#### Step 3: Test Your Agent

In the chat window, try pasting a sample timesheet:

```
Please review these timesheet entries:

1. 2.5 hours - Research - Researched case law
2. 1.0 hours - Drafting - Drafted and revised motion for summary judgment regarding contract breach claims, incorporated recent case precedents
3. 0.3 hours - Administration - Organized files
4. 4.0 hours - Review - Reviewed documents; prepared for meeting; drafted outline; called opposing counsel
5. 0.5 hours - Meeting - Internal team meeting
```

The agent will analyze each entry and tell you which ones need attention.

#### Step 4: Share with Your Team

1. In the Foundry portal, go to your project's **Overview**
2. Assign the **Azure AI User** role to team members who need access
3. Share the project URL — they can chat with the agent directly in the portal!

### Example Conversation

**You:**
> Review this entry: 3.5 hours - Research - Research

**Agent:**
> ❌ **NON-COMPLIANT**
> 
> **Issues found:**
> 1. Description is too vague — "Research" alone doesn't explain what was researched
> 2. 3.5 hours seems excessive for an undescribed research task
> 
> **Suggestions:**
> - Specify what was researched: legal issues, case law, statutes, regulations
> - Include the matter/topic: "Researched Delaware corporate law regarding fiduciary duties"
> - Consider if this should be multiple entries if different topics were researched

### Customizing the Guidelines

Want to use your firm's specific guidelines? Edit the **Instructions** field in the agent playground and replace the guidelines section with your own rules.

### Tips for Best Results

| Do | Don't |
|-----|-------|
| Paste entries in a structured format (numbered list, table) | Paste raw text without structure |
| Include: hours, category, description for each entry | Leave out critical fields |
| Review one day at a time | Paste a month of entries at once |
| Ask follow-up questions | Accept the first response without clarification |

---

## Cloud Architecture: Automatic Timesheet Sync

For enterprise deployments, the tray application can automatically sync timesheet entries to an Azure cloud database. The Foundry agent then accesses entries via an Azure Function API.

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           TIME RECORDING SYSTEM                              │
└─────────────────────────────────────────────────────────────────────────────┘

    ┌───────────────┐         ┌───────────────────┐         ┌──────────────┐
    │   Desktop     │  SYNC   │  Azure Database   │  QUERY  │   Azure      │
    │   Tray App    │────────►│  for PostgreSQL   │◄────────│   Function   │
    │               │         │                   │         │   (API)      │
    └───────────────┘         └───────────────────┘         └──────────────┘
           │                                                        ▲
           │ Local SQLite                                           │ OpenAPI
           │ backup                                                 │
           ▼                                                        │
    ┌───────────────┐                                       ┌──────────────┐
    │  time-        │                                       │  Microsoft   │
    │  tracking.db  │                                       │  Foundry     │
    │               │                                       │  Agent       │
    └───────────────┘                                       └──────────────┘
                                                                    │
                                                                    ▼
                                                            ┌──────────────┐
                                                            │   Users      │
                                                            │   (Chat UI)  │
                                                            └──────────────┘
```

### Components

1. **Desktop Tray App** — Records time locally and syncs to cloud database
2. **Azure Database for PostgreSQL** — Central storage for all users' timesheet entries
3. **Azure Function** — REST API with OpenAPI spec for the Foundry agent
4. **Microsoft Foundry Agent** — Chatbot that users interact with to verify entries

### Setting Up Cloud Sync

#### Step 1: Create Azure Database for PostgreSQL

```bash
# Create resource group
az group create --name rg-timerecording --location eastus

# Create PostgreSQL Flexible Server
az postgres flexible-server create \
  --resource-group rg-timerecording \
  --name timerecording-db \
  --location eastus \
  --admin-user adminuser \
  --admin-password "YourSecurePassword123!" \
  --sku-name Standard_B1ms \
  --storage-size 32 \
  --version 16

# Create the timesheets database
az postgres flexible-server db create \
  --resource-group rg-timerecording \
  --server-name timerecording-db \
  --database-name timesheets

# Allow Azure services to connect
az postgres flexible-server firewall-rule create \
  --resource-group rg-timerecording \
  --name timerecording-db \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

#### Step 2: Configure the Desktop App

Right-click the tray icon and select **"Configure Cloud Sync..."** to open the settings dialog:

1. **Enable Cloud Sync** — Check to enable automatic syncing
2. **Database Provider** — Select "Azure Database for PostgreSQL"
3. **Server Host** — Enter your PostgreSQL server (e.g., `timerecording-db.postgres.database.azure.com`)
4. **Database Name** — Enter `timesheets`
5. **Username** — Your PostgreSQL admin username
6. **Password** — Your PostgreSQL password (stored locally, never in source code)
7. **Your User ID** — Your email address (used to identify your entries)
8. **Sync Interval** — How often to sync (15 minutes recommended)
9. Click **"Test Connection"** to verify the settings work
10. Click **"Save"** to store the configuration

Settings are saved to `data/settings.json` in the application directory. The app will automatically create the required database schema on first sync.

| Setting | Description |
|---------|-------------|
| `Enabled` | Turn cloud sync on/off |
| `Provider` | `PostgreSQL` or `AzureSQL` |
| `Server Host` | Your Azure PostgreSQL server hostname |
| `UserId` | Unique identifier for this user (typically email) |
| `SyncIntervalMinutes` | How often to sync (default: 15 minutes) |
| `SyncApprovedOnly` | Only sync entries marked as approved |

#### Step 3: Deploy the Azure Function

```bash
# Navigate to the Functions project
cd src/TimeRecordingAgent.Functions

# Create a Function App in Azure
az functionapp create \
  --resource-group rg-timerecording \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --functions-version 4 \
  --name timerecording-api \
  --storage-account timerecordingstore

# Configure the connection string
az functionapp config appsettings set \
  --resource-group rg-timerecording \
  --name timerecording-api \
  --settings PostgreSQL__ConnectionString="Host=timerecording-db.postgres.database.azure.com;Database=timesheets;Username=adminuser;Password=YourSecurePassword123!;SSL Mode=Require"

# Set an API key for security
az functionapp config appsettings set \
  --resource-group rg-timerecording \
  --name timerecording-api \
  --settings API_KEY="your-secret-api-key-here"

# Deploy the function
func azure functionapp publish timerecording-api
```

#### Step 4: API Endpoints

The Azure Function exposes these endpoints:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/timesheets/{userId}/{date}` | GET | Get entries for a user on a specific date |
| `/api/timesheets/{userId}/unverified` | GET | Get entries pending verification |
| `/api/timesheets/{userId}/summary` | GET | Get summary stats for a date range |
| `/api/timesheets/verify/{entryId}` | POST | Update verification status |

All endpoints require the `x-api-key` header with your API key.

**Example Request:**
```bash
curl -H "x-api-key: your-secret-api-key" \
  "https://timerecording-api.azurewebsites.net/api/timesheets/user@company.com/2024-01-15"
```

**Example Response:**
```json
[
  {
    "id": 123,
    "startedAt": "2024-01-15T09:00:00Z",
    "endedAt": "2024-01-15T10:30:00Z",
    "durationHours": 1.5,
    "processName": "WINWORD",
    "documentName": "Contract Review - Acme Corp.docx",
    "billableCategory": "Document Review",
    "description": "Reviewed vendor contract terms",
    "verificationStatus": null
  }
]
```

#### Step 5: Connect Foundry Agent to the API

1. In Microsoft Foundry, go to your agent's configuration
2. Add a new **Tool** of type **OpenAPI**
3. Configure the tool:
   - **Name**: `Timesheet API`
   - **Description**: `Retrieves and updates timesheet entries for verification`
   - **Spec URL**: Your Function App URL + `/api/openapi.json`
   - **Authentication**: API Key → Header → `x-api-key` → your API key

4. Update the agent's **Instructions** to reference the tool:

```
You are a legal billing compliance assistant with access to the Timesheet API.

When a user asks to review their entries:
1. Ask for their user ID (email) if not provided
2. Use the GetTimesheetEntries or GetUnverifiedEntries endpoint to fetch their data
3. Analyze each entry against the compliance guidelines
4. Use UpdateVerificationStatus to mark entries as Compliant, NeedsReview, or NonCompliant

Always explain what issues you found and how to fix them.
```

### Database Schema

The sync service creates this table automatically:

```sql
CREATE TABLE timesheet_entries (
    id BIGSERIAL PRIMARY KEY,
    local_id BIGINT NOT NULL,
    user_id VARCHAR(255) NOT NULL,
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ NOT NULL,
    duration_hours DECIMAL(10,2) NOT NULL,
    process_name VARCHAR(255) NOT NULL,
    document_name VARCHAR(500) NOT NULL,
    window_title VARCHAR(1000),
    group_name VARCHAR(255),
    is_billable BOOLEAN NOT NULL DEFAULT TRUE,
    billable_category VARCHAR(100),
    description TEXT,
    is_approved BOOLEAN NOT NULL DEFAULT FALSE,
    verification_status VARCHAR(50),  -- NULL, 'Compliant', 'NeedsReview', 'NonCompliant'
    verification_notes TEXT,
    synced_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(user_id, local_id)
);
```

### Security Considerations

| Concern | Recommendation |
|---------|----------------|
| Database credentials | Use Azure Key Vault references or Managed Identity |
| API authentication | Always set a strong API_KEY in production |
| Network access | Restrict PostgreSQL firewall to Azure services only |
| User data | Consider encrypting sensitive fields at rest |
| CORS | Configure Function App CORS for your Foundry domain |



using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TimeRecordingAgent.Core.Configuration;
using TimeRecordingAgent.Core.Models;

namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Service that uses Microsoft Foundry Agent to verify timesheets against outside counsel guidelines.
/// </summary>
public sealed class FoundryVerificationService
{
    private readonly ILogger<FoundryVerificationService> _logger;
    private readonly object _lock = new();
    private AIAgent? _agent;
    private bool _isConfigured;
    private bool _isEnabled;

    private const string DefaultOutsideCounselGuidelines = @"
Outside Counsel Guidelines for Time Entry Verification:

1. DESCRIPTION REQUIREMENTS
   - Each entry must have a clear, specific description of work performed
   - Avoid vague descriptions like 'work on matter', 'review documents', 'research'
   - Include specific details: what was reviewed, who was communicated with, what was drafted
   - Minimum 10 words for descriptions on entries over 0.5 hours

2. BLOCK BILLING PROHIBITED
   - Do not combine multiple distinct tasks in a single entry
   - Each task should be recorded separately
   - Example of block billing (NOT ALLOWED): 'Draft motion; review opposing counsel's response; prepare for hearing'
   - Correct approach: Separate entries for each task

3. TIME INCREMENT GUIDELINES
   - Minimum billing increment: 0.1 hours (6 minutes)
   - Maximum single entry: 8 hours (longer entries need justification)
   - Time should be proportionate to the task described

4. BILLABLE VS NON-BILLABLE
   - Administrative tasks (scheduling, filing, organizing) are typically non-billable
   - Training and education on client-specific matters may be billable
   - Internal meetings without client involvement are typically non-billable
   - Travel time: first hour typically non-billable

5. CATEGORY REQUIREMENTS
   - Category must accurately reflect the type of work performed
   - Common categories: Research, Drafting, Review, Communication, Meeting, Court Appearance
   - Category should match the description provided

6. PROHIBITED BILLING PRACTICES
   - Do not bill for correcting your own errors
   - Do not bill for staffing decisions
   - Do not bill for administrative overhead
   - Do not bill duplicative time for same task by multiple attorneys without justification";

    private const string SystemPrompt = @"You are an AI assistant specialized in reviewing legal timesheets for compliance with outside counsel guidelines.

Your role is to:
1. Analyze each time entry for compliance with billing guidelines
2. Identify issues such as vague descriptions, block billing, excessive time, and incorrect categorization
3. Suggest improvements for non-compliant entries
4. Determine whether entries should be billable or non-billable

When analyzing entries, consider:
- Is the description specific enough to understand what work was performed?
- Does the time recorded seem reasonable for the task?
- Is the category appropriate for the work described?
- Are there signs of block billing (multiple tasks in one entry)?
- Should this work be billable to the client?

Respond ONLY with a JSON object in this exact format, no additional text:
{
  ""overallStatus"": ""Compliant"" or ""NeedsReview"" or ""NonCompliant"",
  ""summary"": ""Brief summary of overall findings"",
  ""totalBillableHours"": <number>,
  ""totalNonBillableHours"": <number>,
  ""entryResults"": [
    {
      ""activityId"": <long>,
      ""category"": ""string or null"",
      ""description"": ""string or null"",
      ""durationHours"": <number>,
      ""isBillable"": true/false,
      ""status"": ""Compliant"" or ""NeedsReview"" or ""NonCompliant"",
      ""issues"": [
        {
          ""type"": ""VagueDescription|BlockBilling|ExcessiveTimeIncrement|CategoryMismatch|QuestionableBillability|PotentialDuplicate|AdministrativeTaskBilled|MissingInformation|Other"",
          ""severity"": ""Low|Medium|High"",
          ""description"": ""Description of the issue"",
          ""guidelineReference"": ""Reference to specific guideline violated or null""
        }
      ],
      ""suggestions"": [""suggestion 1"", ""suggestion 2""]
    }
  ]
}";

    /// <summary>
    /// Creates a new Foundry Verification Service.
    /// </summary>
    public FoundryVerificationService(ILogger<FoundryVerificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new Foundry Verification Service with settings.
    /// </summary>
    public FoundryVerificationService(ILogger<FoundryVerificationService> logger, FoundryAgentSettings settings)
        : this(logger)
    {
        Configure(settings);
    }

    /// <summary>
    /// Gets whether the verification service is available and enabled.
    /// </summary>
    public bool IsEnabled => _isEnabled && _isConfigured;

    /// <summary>
    /// Gets whether the service has been configured with valid settings.
    /// </summary>
    public bool IsConfigured => _isConfigured;

    /// <summary>
    /// Configures or reconfigures the service with new settings.
    /// </summary>
    public void Configure(FoundryAgentSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.Enabled;

            if (!settings.IsConfigured)
            {
                _logger.LogInformation("Foundry Verification is not configured: missing project endpoint.");
                _isConfigured = false;
                _agent = null;
                return;
            }

            try
            {
                var endpoint = new Uri(settings.ProjectEndpoint!);
                var deploymentName = settings.DeploymentName ?? "gpt-4o-mini";
                var guidelines = settings.OutsideCounselGuidelines ?? DefaultOutsideCounselGuidelines;

                // Create the AI Agent using Microsoft Agent Framework
                // Using Azure CLI credential for authentication (user should run 'az login' first)
                var chatClient = new AzureOpenAIClient(endpoint, new AzureCliCredential())
                    .GetChatClient(deploymentName)
                    .AsIChatClient();

                _agent = new ChatClientAgent(
                    chatClient,
                    instructions: $"{SystemPrompt}\n\n## OUTSIDE COUNSEL GUIDELINES TO VERIFY AGAINST:\n{guidelines}",
                    name: "TimesheetVerificationAgent");

                _isConfigured = true;
                _logger.LogInformation(
                    "Foundry Verification service configured with endpoint: {Endpoint}, deployment: {Deployment}, enabled: {Enabled}",
                    endpoint,
                    deploymentName,
                    _isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure Foundry Verification service");
                _isConfigured = false;
                _agent = null;
            }
        }
    }

    /// <summary>
    /// Verifies a list of activity records against outside counsel guidelines.
    /// </summary>
    /// <param name="entries">The time entries to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification result.</returns>
    public async Task<TimesheetVerificationResult?> VerifyTimesheetAsync(
        IReadOnlyList<ActivityRecord> entries,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || _agent is null)
        {
            _logger.LogWarning("Foundry Verification is not enabled or configured.");
            return null;
        }

        if (entries.Count == 0)
        {
            _logger.LogInformation("No entries to verify.");
            return new TimesheetVerificationResult(
                ComplianceStatus.Compliant,
                "No entries to verify.",
                [],
                0,
                0,
                DateTime.UtcNow);
        }

        try
        {
            // Build the prompt with timesheet data
            var entriesJson = BuildEntriesJson(entries);
            var prompt = $@"Please verify the following timesheet entries against the outside counsel guidelines:

{entriesJson}

Analyze each entry for compliance issues and provide your assessment in the required JSON format.";

            _logger.LogDebug("Sending {Count} entries to Foundry agent for verification.", entries.Count);

            // Call the agent
            var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);
            var responseText = response.Text;

            _logger.LogDebug("Received verification response from Foundry agent.");

            // Parse the JSON response
            var result = ParseVerificationResponse(responseText, entries);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify timesheet with Foundry agent.");
            return null;
        }
    }

    /// <summary>
    /// Verifies entries for a specific date.
    /// </summary>
    public async Task<TimesheetVerificationResult?> VerifyDailyTimesheetAsync(
        IEnumerable<ActivityRecord> allEntries,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var dateStart = date.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var dateEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();

        var dailyEntries = allEntries
            .Where(e => e.StartedAtUtc >= dateStart && e.StartedAtUtc < dateEnd)
            .ToList();

        return await VerifyTimesheetAsync(dailyEntries, cancellationToken);
    }

    private static string BuildEntriesJson(IReadOnlyList<ActivityRecord> entries)
    {
        var entriesData = entries.Select(e => new
        {
            activityId = e.Id,
            date = e.StartedAtLocal.ToString("yyyy-MM-dd"),
            startTime = e.StartedAtLocal.ToString("HH:mm"),
            endTime = e.EndedAtLocal.ToString("HH:mm"),
            durationHours = Math.Round(e.Duration.TotalHours, 2),
            category = e.BillableCategory ?? "Unclassified",
            description = e.Description ?? $"Work in {e.ProcessName}: {e.DocumentName}",
            isBillable = e.IsBillable,
            processName = e.ProcessName,
            documentName = e.DocumentName,
            groupName = e.GroupName
        });

        return JsonSerializer.Serialize(entriesData, new JsonSerializerOptions { WriteIndented = true });
    }

    private TimesheetVerificationResult ParseVerificationResponse(string responseText, IReadOnlyList<ActivityRecord> entries)
    {
        try
        {
            // Try to extract JSON from the response (in case there's extra text)
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var overallStatus = ParseComplianceStatus(root.GetProperty("overallStatus").GetString());
            var summary = root.GetProperty("summary").GetString() ?? "Verification complete.";
            var totalBillable = root.TryGetProperty("totalBillableHours", out var billableEl) ? billableEl.GetDouble() : 0;
            var totalNonBillable = root.TryGetProperty("totalNonBillableHours", out var nonBillableEl) ? nonBillableEl.GetDouble() : 0;

            var entryResults = new List<EntryVerificationResult>();
            if (root.TryGetProperty("entryResults", out var resultsEl))
            {
                foreach (var entryEl in resultsEl.EnumerateArray())
                {
                    var activityId = entryEl.GetProperty("activityId").GetInt64();
                    var category = entryEl.TryGetProperty("category", out var catEl) ? catEl.GetString() : null;
                    var description = entryEl.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
                    var durationHours = entryEl.TryGetProperty("durationHours", out var durEl) ? durEl.GetDouble() : 0;
                    var isBillable = entryEl.TryGetProperty("isBillable", out var billEl) && billEl.GetBoolean();
                    var status = ParseComplianceStatus(entryEl.GetProperty("status").GetString());

                    var issues = new List<ComplianceIssue>();
                    if (entryEl.TryGetProperty("issues", out var issuesEl))
                    {
                        foreach (var issueEl in issuesEl.EnumerateArray())
                        {
                            issues.Add(new ComplianceIssue(
                                ParseIssueType(issueEl.GetProperty("type").GetString()),
                                ParseSeverity(issueEl.GetProperty("severity").GetString()),
                                issueEl.GetProperty("description").GetString() ?? "Issue detected",
                                issueEl.TryGetProperty("guidelineReference", out var refEl) ? refEl.GetString() : null
                            ));
                        }
                    }

                    var suggestions = new List<string>();
                    if (entryEl.TryGetProperty("suggestions", out var suggestionsEl))
                    {
                        foreach (var suggEl in suggestionsEl.EnumerateArray())
                        {
                            var suggestion = suggEl.GetString();
                            if (!string.IsNullOrWhiteSpace(suggestion))
                            {
                                suggestions.Add(suggestion);
                            }
                        }
                    }

                    entryResults.Add(new EntryVerificationResult(
                        activityId, category, description, durationHours,
                        isBillable, status, issues, suggestions));
                }
            }

            return new TimesheetVerificationResult(
                overallStatus,
                summary,
                entryResults,
                totalBillable,
                totalNonBillable,
                DateTime.UtcNow);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse verification response as JSON. Response: {Response}", responseText);

            // Return a fallback result
            return new TimesheetVerificationResult(
                ComplianceStatus.NeedsReview,
                $"Verification completed but response parsing failed. Raw response available in logs.",
                entries.Select(e => new EntryVerificationResult(
                    e.Id, e.BillableCategory, e.Description, e.Duration.TotalHours,
                    e.IsBillable, ComplianceStatus.NeedsReview, [], [])).ToList(),
                entries.Where(e => e.IsBillable).Sum(e => e.Duration.TotalHours),
                entries.Where(e => !e.IsBillable).Sum(e => e.Duration.TotalHours),
                DateTime.UtcNow);
        }
    }

    private static ComplianceStatus ParseComplianceStatus(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "compliant" => ComplianceStatus.Compliant,
            "noncompliant" => ComplianceStatus.NonCompliant,
            _ => ComplianceStatus.NeedsReview
        };
    }

    private static ComplianceIssueType ParseIssueType(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "vaguedescription" => ComplianceIssueType.VagueDescription,
            "blockbilling" => ComplianceIssueType.BlockBilling,
            "excessivetimeincrement" => ComplianceIssueType.ExcessiveTimeIncrement,
            "categorymismatch" => ComplianceIssueType.CategoryMismatch,
            "questionablebillability" => ComplianceIssueType.QuestionableBillability,
            "potentialduplicate" => ComplianceIssueType.PotentialDuplicate,
            "administrativetaskbilled" => ComplianceIssueType.AdministrativeTaskBilled,
            "missinginformation" => ComplianceIssueType.MissingInformation,
            _ => ComplianceIssueType.Other
        };
    }

    private static IssueSeverity ParseSeverity(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "low" => IssueSeverity.Low,
            "high" => IssueSeverity.High,
            _ => IssueSeverity.Medium
        };
    }
}

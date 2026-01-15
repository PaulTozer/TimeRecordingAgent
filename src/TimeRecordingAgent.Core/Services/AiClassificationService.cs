using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.Inference;
using TimeRecordingAgent.Core.Configuration;

namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Represents the AI-suggested classification for a task.
/// </summary>
public sealed record TaskClassificationSuggestion(
    string? CustomerName,
    string? Category,
    bool IsBillable,
    string? Reasoning,
    string? Description = null);

/// <summary>
/// Service that uses AI to classify tasks and detect customer names from activity context.
/// </summary>
public sealed class AiClassificationService
{
    private readonly ILogger<AiClassificationService> _logger;
    private readonly object _lock = new();
    private ChatCompletionsClient? _client;
    private string _model = "gpt-4o-mini";
    private bool _isEnabled;
    private bool _isConfigured;

    private const string SystemPrompt = @"You are an AI assistant that helps classify work activities for time tracking.
Given information about a user's current work context (window title, document name, process name, and optionally content from the document/email), 
analyze the context and provide:
1. The customer/client name if detectable (look for company names, project names, or client identifiers)
2. A category for the work (Development, Support, Meeting, Administration, Research, Documentation, or suggest another if appropriate)
3. Whether this appears to be billable work (true/false)
4. A brief description of the work being done (1-2 sentences summarizing the task based on available context)

Respond ONLY with a JSON object in this exact format, no additional text:
{
  ""customerName"": ""string or null"",
  ""category"": ""string"",
  ""isBillable"": true or false,
  ""reasoning"": ""brief explanation of your classification"",
  ""description"": ""1-2 sentence description of the work task""
}

Guidelines:
- Customer names often appear in document names, project folders, or window titles
- Internal company meetings, admin tasks, and personal development are typically not billable
- Client work, support tickets, and customer projects are typically billable
- Look for patterns like ""[CustomerName] - Task"", ""CustomerName.Project"", ticket numbers, etc.
- If you cannot determine a customer, set customerName to null
- Be concise in your reasoning
- For description, summarize what work is being done based on the document name, email subject, or content snippet
- If content is provided, use it to give a more specific description";

    private const string VisionSystemPrompt = @"You are an AI assistant that helps classify work activities for time tracking.
You will be shown a screenshot of the user's current work window. Analyze what you see and provide:
1. The customer/client name if visible (look for company names, project names, or client identifiers in the UI)
2. A category for the work (Development, Support, Meeting, Administration, Research, Documentation, or suggest another)
3. Whether this appears to be billable work (true/false)
4. A brief description of what work is being done based on what you see on screen

Respond ONLY with a JSON object in this exact format, no additional text:
{
  ""customerName"": ""string or null"",
  ""category"": ""string"",
  ""isBillable"": true or false,
  ""reasoning"": ""brief explanation of your classification"",
  ""description"": ""1-2 sentence description of the work task based on what you see""
}

Guidelines:
- Look for customer/client names in window titles, document headers, email addresses, chat participant names
- Teams meetings, Zoom calls with external participants are often billable
- Internal team chats, personal browsing, and admin tasks are typically not billable
- For meetings, describe what the meeting appears to be about based on visible content
- For documents, describe what the document appears to contain
- Be concise but specific based on visible text and UI elements";

    /// <summary>
    /// Creates a new AI Classification Service.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public AiClassificationService(ILogger<AiClassificationService> logger)
    {
        _logger = logger;
        _isEnabled = false;
        _isConfigured = false;
    }

    /// <summary>
    /// Creates a new AI Classification Service with settings.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="settings">The Azure AI settings.</param>
    public AiClassificationService(ILogger<AiClassificationService> logger, AzureAiSettings settings)
        : this(logger)
    {
        Configure(settings);
    }

    /// <summary>
    /// Gets whether the AI classification service is available and enabled.
    /// </summary>
    public bool IsEnabled => _isEnabled && _isConfigured;

    /// <summary>
    /// Gets whether the AI service has been configured with valid settings.
    /// </summary>
    public bool IsConfigured => _isConfigured;

    /// <summary>
    /// Configures or reconfigures the AI service with new settings.
    /// </summary>
    /// <param name="settings">The Azure AI settings.</param>
    public void Configure(AzureAiSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.Enabled;
            
            if (!settings.IsConfigured)
            {
                _logger.LogInformation("AI Classification is not configured: missing endpoint or API key.");
                _isConfigured = false;
                _client = null;
                return;
            }

            try
            {
                _model = settings.Model ?? "gpt-4o-mini";
                var constructedEndpoint = ConstructEndpointUrl(settings.Endpoint!, _model);
                var uri = new Uri(constructedEndpoint);
                var credential = new AzureKeyCredential(settings.ApiKey!);
                _client = new ChatCompletionsClient(uri, credential, new AzureAIInferenceClientOptions());
                _isConfigured = true;
                _logger.LogInformation(
                    "AI Classification service configured with endpoint: {Endpoint}, model: {Model}, enabled: {Enabled}", 
                    constructedEndpoint, 
                    _model,
                    _isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure AI Classification service");
                _isConfigured = false;
                _client = null;
            }
        }
    }

    /// <summary>
    /// Cleans up common mistakes in endpoint URLs.
    /// </summary>
    private static string CleanEndpointUrl(string endpoint)
    {
        var cleaned = endpoint.Trim();
        
        // Remove query string (e.g., ?api-version=...)
        var queryIndex = cleaned.IndexOf('?');
        if (queryIndex > 0)
        {
            cleaned = cleaned[..queryIndex];
        }
        
        // Remove trailing /chat/completions path that SDK adds automatically
        if (cleaned.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^"/chat/completions".Length];
        }
        
        // Remove /openai/responses path (Responses API - not what we use)
        if (cleaned.EndsWith("/openai/responses", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^"/openai/responses".Length];
        }
        
        // Remove /openai suffix if present
        if (cleaned.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^"/openai".Length];
        }
        
        // Remove trailing slash
        cleaned = cleaned.TrimEnd('/');
        
        return cleaned;
    }

    /// <summary>
    /// Constructs the full endpoint URL, adding deployment path for Azure OpenAI if needed.
    /// </summary>
    private static string ConstructEndpointUrl(string baseEndpoint, string model)
    {
        var cleaned = CleanEndpointUrl(baseEndpoint);
        
        // For Azure OpenAI (cognitiveservices.azure.com or openai.azure.com), add deployment path
        if ((cleaned.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) ||
             cleaned.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase)) &&
            !cleaned.Contains("/openai/deployments/", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = $"{cleaned}/openai/deployments/{model}";
        }
        
        return cleaned;
    }

    /// <summary>
    /// Sets whether AI suggestions are enabled (without reconfiguring).
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            _isEnabled = enabled;
            _logger.LogInformation("AI suggestions {State}.", enabled ? "enabled" : "disabled");
        }
    }

    /// <summary>
    /// Classifies a task based on the provided context using AI.
    /// </summary>
    /// <param name="processName">The name of the active process.</param>
    /// <param name="windowTitle">The window title.</param>
    /// <param name="documentName">The document or file name.</param>
    /// <param name="contentSnippet">Optional snippet of content from the document/email for better description generation.</param>
    /// <param name="previousCustomers">Optional list of previously used customer names to help with matching.</param>
    /// <returns>The AI's classification suggestion, or null if classification fails.</returns>
    public async Task<TaskClassificationSuggestion?> ClassifyTaskAsync(
        string processName, 
        string windowTitle, 
        string documentName,
        string? contentSnippet = null,
        IEnumerable<string>? previousCustomers = null)
    {
        if (!_isEnabled || _client is null)
        {
            _logger.LogDebug("AI Classification skipped: service is not enabled");
            return null;
        }

        try
        {
            var userPrompt = BuildUserPrompt(processName, windowTitle, documentName, contentSnippet, previousCustomers);
            
            var requestOptions = new ChatCompletionsOptions
            {
                Messages =
                {
                    new ChatRequestSystemMessage(SystemPrompt),
                    new ChatRequestUserMessage(userPrompt)
                },
                Model = _model
            };

            _logger.LogDebug("Requesting AI classification for document: {Document}", documentName);
            
            var response = await _client.CompleteAsync(requestOptions);
            var content = response.Value.Content;
            
            _logger.LogTrace("AI response: {Response}", content);
            
            return ParseResponse(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI classification failed for document: {Document}", documentName);
            return null;
        }
    }

    /// <summary>
    /// Synchronous version of ClassifyTaskAsync for UI thread convenience.
    /// </summary>
    public TaskClassificationSuggestion? ClassifyTask(
        string processName,
        string windowTitle,
        string documentName,
        string? contentSnippet = null,
        IEnumerable<string>? previousCustomers = null)
    {
        try
        {
            return ClassifyTaskAsync(processName, windowTitle, documentName, contentSnippet, previousCustomers)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Synchronous AI classification failed");
            return null;
        }
    }

    /// <summary>
    /// Classifies a task using a screenshot of the active window (vision-based).
    /// </summary>
    /// <param name="processName">The name of the active process.</param>
    /// <param name="windowTitle">The window title.</param>
    /// <param name="documentName">The document or file name.</param>
    /// <param name="screenshotBase64">Base64-encoded JPEG screenshot of the window.</param>
    /// <param name="previousCustomers">Optional list of previously used customer names.</param>
    /// <returns>The AI's classification suggestion, or null if classification fails.</returns>
    public async Task<TaskClassificationSuggestion?> ClassifyTaskWithScreenshotAsync(
        string processName,
        string windowTitle,
        string documentName,
        string screenshotBase64,
        IEnumerable<string>? previousCustomers = null)
    {
        if (!_isEnabled || _client is null)
        {
            _logger.LogDebug("AI Classification skipped: service is not enabled");
            return null;
        }

        try
        {
            var textContent = $"Process: {processName}\nWindow Title: {windowTitle}\nDocument: {documentName}";
            
            if (previousCustomers?.Any() == true)
            {
                textContent += $"\n\nKnown customers (for reference): {string.Join(", ", previousCustomers.Take(10))}";
            }

            // Build multi-modal message with text and image
            var userMessageContent = new List<ChatMessageContentItem>
            {
                new ChatMessageTextContentItem(textContent),
                new ChatMessageImageContentItem(
                    new Uri($"data:image/jpeg;base64,{screenshotBase64}"))
            };

            var requestOptions = new ChatCompletionsOptions
            {
                Messages =
                {
                    new ChatRequestSystemMessage(VisionSystemPrompt),
                    new ChatRequestUserMessage(userMessageContent)
                },
                Model = _model
            };

            _logger.LogDebug("Requesting AI vision classification for document: {Document}", documentName);
            
            var response = await _client.CompleteAsync(requestOptions);
            var content = response.Value.Content;
            
            _logger.LogTrace("AI vision response: {Response}", content);
            
            return ParseResponse(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI vision classification failed for document: {Document}", documentName);
            return null;
        }
    }

    private static string BuildUserPrompt(
        string processName, 
        string windowTitle, 
        string documentName,
        string? contentSnippet,
        IEnumerable<string>? previousCustomers)
    {
        var prompt = $@"Classify this work activity:

Process: {processName}
Window Title: {windowTitle}
Document/File: {documentName}";

        if (!string.IsNullOrWhiteSpace(contentSnippet))
        {
            prompt += $@"

Content snippet (from document/email body):
{contentSnippet}";
        }

        if (previousCustomers?.Any() == true)
        {
            prompt += $@"

Known customers from previous entries (for reference):
{string.Join(", ", previousCustomers.Take(10))}";
        }

        return prompt;
    }

    private TaskClassificationSuggestion? ParseResponse(string content)
    {
        try
        {
            // Extract JSON from the response (in case there's any extra text)
            var jsonMatch = Regex.Match(content, @"\{[\s\S]*\}", RegexOptions.Multiline);
            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not extract JSON from AI response: {Content}", content);
                return null;
            }

            var json = jsonMatch.Value;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var customerName = root.TryGetProperty("customerName", out var cn) && cn.ValueKind != JsonValueKind.Null 
                ? cn.GetString() 
                : null;
            
            var category = root.TryGetProperty("category", out var cat) 
                ? cat.GetString() ?? "Development" 
                : "Development";
            
            var isBillable = root.TryGetProperty("isBillable", out var bill) && bill.GetBoolean();
            
            var reasoning = root.TryGetProperty("reasoning", out var reason) 
                ? reason.GetString() 
                : null;

            var description = root.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString()
                : null;

            return new TaskClassificationSuggestion(customerName, category, isBillable, reasoning, description);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response as JSON: {Content}", content);
            return null;
        }
    }
}

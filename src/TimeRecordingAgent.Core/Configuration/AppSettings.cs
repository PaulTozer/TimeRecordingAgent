using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeRecordingAgent.Core.Configuration;

/// <summary>
/// Application settings that are persisted to a JSON configuration file.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Azure AI / Microsoft Foundry settings.
    /// </summary>
    public AzureAiSettings AzureAi { get; set; } = new();

    /// <summary>
    /// General application settings.
    /// </summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>
    /// Loads settings from the specified file path, or creates default settings if the file doesn't exist.
    /// </summary>
    public static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // If loading fails, return default settings
        }

        return new AppSettings();
    }

    /// <summary>
    /// Saves settings to the specified file path.
    /// </summary>
    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }
}

/// <summary>
/// Settings for Azure AI / Microsoft Foundry integration.
/// </summary>
public sealed class AzureAiSettings
{
    /// <summary>
    /// The Microsoft Foundry endpoint URL.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The API key for authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model deployment name (e.g., "gpt-4o-mini").
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Whether AI suggestions are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Returns true if the settings are configured with endpoint and API key.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// General application settings.
/// </summary>
public sealed class GeneralSettings
{
    /// <summary>
    /// Whether task prompts are enabled.
    /// </summary>
    public bool TaskPromptsEnabled { get; set; } = true;

    /// <summary>
    /// The threshold in minutes before prompting for task classification.
    /// </summary>
    public int PromptThresholdMinutes { get; set; } = 5;
}

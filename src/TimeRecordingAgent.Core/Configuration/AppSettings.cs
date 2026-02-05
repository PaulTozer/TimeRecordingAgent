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
    /// Azure AI / Microsoft Foundry settings for classification.
    /// </summary>
    public AzureAiSettings AzureAi { get; set; } = new();

    /// <summary>
    /// Microsoft Foundry Agent settings for timesheet verification.
    /// </summary>
    public FoundryAgentSettings FoundryAgent { get; set; } = new();

    /// <summary>
    /// Cloud database sync settings for pushing entries to Azure.
    /// </summary>
    public CloudSyncSettings CloudSync { get; set; } = new();

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

/// <summary>
/// Settings for Microsoft Foundry Agent used for timesheet verification.
/// </summary>
public sealed class FoundryAgentSettings
{
    /// <summary>
    /// The Microsoft Foundry project endpoint URL.
    /// Example: https://your-project.services.ai.azure.com
    /// </summary>
    public string? ProjectEndpoint { get; set; }

    /// <summary>
    /// The model deployment name (e.g., "gpt-4o", "gpt-4o-mini").
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Whether timesheet verification is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom outside counsel guidelines to verify against.
    /// If null, default guidelines will be used.
    /// </summary>
    public string? OutsideCounselGuidelines { get; set; }

    /// <summary>
    /// Returns true if the settings are configured with a project endpoint.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectEndpoint);
}

/// <summary>
/// Settings for syncing timesheet data to Azure cloud database.
/// </summary>
public sealed class CloudSyncSettings
{
    /// <summary>
    /// Whether cloud sync is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// The database provider type.
    /// </summary>
    public CloudDatabaseProvider Provider { get; set; } = CloudDatabaseProvider.PostgreSQL;

    /// <summary>
    /// The connection string for the Azure database.
    /// For PostgreSQL: "Host=myserver.postgres.database.azure.com;Database=timesheets;Username=user;Password=pass;SSL Mode=Require"
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The user identifier to associate with synced entries (e.g., email or employee ID).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// How often to sync entries to the cloud (in minutes).
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Whether to only sync approved entries.
    /// </summary>
    public bool SyncApprovedOnly { get; set; } = false;

    /// <summary>
    /// Returns true if the settings are configured for cloud sync.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(UserId);
}

/// <summary>
/// Supported cloud database providers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloudDatabaseProvider
{
    /// <summary>
    /// Azure Database for PostgreSQL.
    /// </summary>
    PostgreSQL,

    /// <summary>
    /// Azure SQL Database.
    /// </summary>
    AzureSQL
}

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TimeRecordingAgent.Core.Configuration;
using Azure;
using Azure.AI.Inference;

namespace TimeRecordingAgent.App.Dialogs;

public partial class AiSettingsDialog : Window
{
    private readonly AzureAiSettings _settings;
    private bool _showingKey = false;
    private string? _apiKeyValue;

    public AiSettingsDialog(AzureAiSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        
        // Load current settings into UI
        EnabledCheckbox.IsChecked = settings.Enabled;
        EndpointTextBox.Text = settings.Endpoint ?? "";
        _apiKeyValue = settings.ApiKey;
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            ApiKeyPasswordBox.Password = settings.ApiKey;
        }
        ModelCombo.Text = settings.Model ?? "gpt-4o-mini";
        
        UpdateStatus();
    }

    /// <summary>
    /// Gets whether the settings were saved.
    /// </summary>
    public bool WasSaved { get; private set; }

    /// <summary>
    /// Gets the updated settings.
    /// </summary>
    public AzureAiSettings UpdatedSettings => new()
    {
        Enabled = EnabledCheckbox.IsChecked ?? true,
        Endpoint = string.IsNullOrWhiteSpace(EndpointTextBox.Text) ? null : EndpointTextBox.Text.Trim(),
        ApiKey = string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password) ? null : ApiKeyPasswordBox.Password,
        Model = string.IsNullOrWhiteSpace(ModelCombo.Text) ? "gpt-4o-mini" : ModelCombo.Text.Trim()
    };

    private void UpdateStatus()
    {
        var settings = UpdatedSettings;
        
        if (!settings.Enabled)
        {
            StatusBorder.Visibility = Visibility.Visible;
            StatusBorder.Background = System.Windows.Media.Brushes.LightGray;
            StatusBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGray;
            StatusText.Text = "AI suggestions are disabled.";
        }
        else if (!settings.IsConfigured)
        {
            StatusBorder.Visibility = Visibility.Visible;
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 243, 205));
            StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 193, 7));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(133, 100, 4));
            StatusText.Text = "Please enter the endpoint URL and API key to enable AI suggestions.";
        }
        else
        {
            StatusBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowKeyButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle between showing and hiding the API key
        // Note: WPF PasswordBox doesn't support showing password directly,
        // so we'll just show a tooltip with masked value
        _showingKey = !_showingKey;
        ShowKeyButton.Content = _showingKey ? "🙈" : "👁";
        
        if (_showingKey && !string.IsNullOrEmpty(ApiKeyPasswordBox.Password))
        {
            var key = ApiKeyPasswordBox.Password;
            var masked = key.Length > 8 
                ? key[..4] + "..." + key[^4..] 
                : "****";
            ShowKeyButton.ToolTip = $"Key: {masked}";
        }
        else
        {
            ShowKeyButton.ToolTip = "Show/hide API key";
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = UpdatedSettings;
        
        if (!settings.IsConfigured)
        {
            ShowTestResult(false, "Please enter the endpoint URL and API key first.");
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "Testing...";

        try
        {
            var result = await TestConnectionAsync(settings);
            ShowTestResult(result.Success, result.Message);
        }
        catch (Exception ex)
        {
            ShowTestResult(false, $"Error: {ex.Message}");
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "Test Connection";
        }
    }

    private async Task<(bool Success, string Message)> TestConnectionAsync(AzureAiSettings settings)
    {
        try
        {
            var cleanedEndpoint = CleanEndpointUrl(settings.Endpoint!);
            
            // For Azure OpenAI (cognitiveservices.azure.com), construct the full deployment URL
            if (cleanedEndpoint.Contains("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) ||
                cleanedEndpoint.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                // Azure OpenAI requires the deployment path
                if (!cleanedEndpoint.Contains("/openai/deployments/", StringComparison.OrdinalIgnoreCase))
                {
                    // Append the deployment path using the model name as deployment name
                    cleanedEndpoint = $"{cleanedEndpoint}/openai/deployments/{settings.Model}";
                }
            }
            
            var endpoint = new Uri(cleanedEndpoint);
            var credential = new AzureKeyCredential(settings.ApiKey!);
            var client = new ChatCompletionsClient(endpoint, credential, new AzureAIInferenceClientOptions());

            var requestOptions = new ChatCompletionsOptions
            {
                Messages =
                {
                    new ChatRequestSystemMessage("You are a helpful assistant."),
                    new ChatRequestUserMessage("Say 'Hello' in one word.")
                },
                Model = settings.Model
            };

            var response = await client.CompleteAsync(requestOptions);
            
            if (response?.Value != null)
            {
                return (true, $"✓ Connection successful! Model responded: \"{response.Value.Content}\"");
            }
            
            return (false, "Connection failed: No response received.");
        }
        catch (RequestFailedException ex)
        {
            var hint = ex.Status == 404 
                ? "\n\nHint: For Azure OpenAI, enter just the base URL:\n• https://<resource>.cognitiveservices.azure.com\n• https://<resource>.openai.azure.com\n\nThe deployment path will be added automatically using the Model name.\n\nDo NOT include /openai/responses, /chat/completions or ?api-version=..."
                : "";
            return (false, $"API Error ({ex.Status}): {ex.Message}{hint}");
        }
        catch (UriFormatException)
        {
            return (false, "Invalid endpoint URL format.\n\nExpected format:\nhttps://<resource>.cognitiveservices.azure.com\n\nThe deployment path will be added automatically using the Model name.");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
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
        
        // Remove /openai/responses path (new Responses API - not compatible with this SDK)
        if (cleaned.EndsWith("/openai/responses", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^"/openai/responses".Length];
        }
        
        // Remove bare /openai path
        if (cleaned.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^"/openai".Length];
        }
        
        // Remove trailing slash
        cleaned = cleaned.TrimEnd('/');
        
        return cleaned;
    }

    private void ShowTestResult(bool success, string message)
    {
        StatusBorder.Visibility = Visibility.Visible;
        
        if (success)
        {
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(212, 237, 218));
            StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(40, 167, 69));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(21, 87, 36));
        }
        else
        {
            StatusBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(248, 215, 218));
            StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(220, 53, 69));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(114, 28, 36));
        }
        
        StatusText.Text = message;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = false;
        DialogResult = false;
        Close();
    }
}

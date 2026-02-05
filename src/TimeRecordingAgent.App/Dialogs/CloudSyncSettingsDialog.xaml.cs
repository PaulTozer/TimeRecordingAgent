using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Npgsql;
using TimeRecordingAgent.Core.Configuration;

namespace TimeRecordingAgent.App.Dialogs;

public partial class CloudSyncSettingsDialog : Window
{
    private readonly CloudSyncSettings _settings;
    private bool _showingPassword = false;

    public CloudSyncSettingsDialog(CloudSyncSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        
        // Load current settings into UI
        EnabledCheckbox.IsChecked = settings.Enabled;
        UserIdTextBox.Text = settings.UserId ?? "";
        SyncApprovedOnlyCheckbox.IsChecked = settings.SyncApprovedOnly;
        
        // Set provider
        foreach (ComboBoxItem item in ProviderCombo.Items)
        {
            if (item.Tag?.ToString() == settings.Provider.ToString())
            {
                ProviderCombo.SelectedItem = item;
                break;
            }
        }
        
        // Set interval
        foreach (ComboBoxItem item in IntervalCombo.Items)
        {
            if (item.Tag?.ToString() == settings.SyncIntervalMinutes.ToString())
            {
                IntervalCombo.SelectedItem = item;
                break;
            }
        }
        
        // Parse connection string if present
        if (!string.IsNullOrEmpty(settings.ConnectionString))
        {
            ParseConnectionString(settings.ConnectionString);
        }
        
        UpdateStatus();
    }

    /// <summary>
    /// Gets whether the settings were saved.
    /// </summary>
    public bool WasSaved { get; private set; }

    /// <summary>
    /// Gets the updated settings.
    /// </summary>
    public CloudSyncSettings UpdatedSettings
    {
        get
        {
            var provider = GetSelectedProvider();
            var interval = GetSelectedInterval();
            
            return new CloudSyncSettings
            {
                Enabled = EnabledCheckbox.IsChecked ?? false,
                Provider = provider,
                ConnectionString = BuildConnectionString(),
                UserId = string.IsNullOrWhiteSpace(UserIdTextBox.Text) ? null : UserIdTextBox.Text.Trim(),
                SyncIntervalMinutes = interval,
                SyncApprovedOnly = SyncApprovedOnlyCheckbox.IsChecked ?? false
            };
        }
    }

    private CloudDatabaseProvider GetSelectedProvider()
    {
        var selectedItem = ProviderCombo.SelectedItem as ComboBoxItem;
        var tag = selectedItem?.Tag?.ToString() ?? "PostgreSQL";
        return Enum.TryParse<CloudDatabaseProvider>(tag, out var provider) 
            ? provider 
            : CloudDatabaseProvider.PostgreSQL;
    }

    private int GetSelectedInterval()
    {
        var selectedItem = IntervalCombo.SelectedItem as ComboBoxItem;
        var tag = selectedItem?.Tag?.ToString() ?? "15";
        return int.TryParse(tag, out var interval) ? interval : 15;
    }

    private void ParseConnectionString(string connectionString)
    {
        try
        {
            // Try to parse as PostgreSQL connection string
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            HostTextBox.Text = builder.Host ?? "";
            DatabaseTextBox.Text = builder.Database ?? "timesheets";
            UsernameTextBox.Text = builder.Username ?? "";
            if (!string.IsNullOrEmpty(builder.Password))
            {
                PasswordBox.Password = builder.Password;
            }
        }
        catch
        {
            // If parsing fails, just leave fields empty
        }
    }

    private string BuildConnectionString()
    {
        var host = HostTextBox.Text?.Trim();
        var database = DatabaseTextBox.Text?.Trim() ?? "timesheets";
        var username = UsernameTextBox.Text?.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
        {
            return "";
        }

        var provider = GetSelectedProvider();
        
        if (provider == CloudDatabaseProvider.PostgreSQL)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Database = database,
                Username = username,
                Password = password,
                SslMode = SslMode.Require
            };
            return builder.ConnectionString;
        }
        else
        {
            // Azure SQL connection string
            return $"Server=tcp:{host},1433;Initial Catalog={database};Persist Security Info=False;User ID={username};Password={password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        }
    }

    private void UpdateStatus()
    {
        var settings = UpdatedSettings;
        
        if (!settings.Enabled)
        {
            StatusBorder.Visibility = Visibility.Visible;
            StatusBorder.Background = System.Windows.Media.Brushes.LightGray;
            StatusBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGray;
            StatusText.Text = "Cloud sync is disabled.";
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
            StatusText.Text = "Please enter the server host, username, password, and your user ID to enable cloud sync.";
        }
        else
        {
            StatusBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        _showingPassword = !_showingPassword;
        ShowPasswordButton.Content = _showingPassword ? "🙈" : "👁";
        
        if (_showingPassword && !string.IsNullOrEmpty(PasswordBox.Password))
        {
            var pwd = PasswordBox.Password;
            var masked = pwd.Length > 8 
                ? pwd[..4] + "..." + pwd[^4..] 
                : "****";
            ShowPasswordButton.ToolTip = $"Password: {masked}";
        }
        else
        {
            ShowPasswordButton.ToolTip = "Show/hide password";
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = UpdatedSettings;
        
        if (!settings.IsConfigured)
        {
            ShowTestResult(false, "Please fill in all required fields first.");
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

    private async Task<(bool Success, string Message)> TestConnectionAsync(CloudSyncSettings settings)
    {
        if (settings.Provider == CloudDatabaseProvider.PostgreSQL)
        {
            try
            {
                await using var connection = new NpgsqlConnection(settings.ConnectionString);
                await connection.OpenAsync();
                var version = connection.ServerVersion;
                return (true, $"✅ Connected successfully!\nPostgreSQL version: {version}");
            }
            catch (Exception ex)
            {
                return (false, $"❌ Connection failed: {ex.Message}");
            }
        }
        else
        {
            return (false, "Azure SQL testing not yet implemented.");
        }
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
        DialogResult = false;
        Close();
    }
}

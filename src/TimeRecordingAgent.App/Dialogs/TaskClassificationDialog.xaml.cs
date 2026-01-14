using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using TimeRecordingAgent.Core.Services;

namespace TimeRecordingAgent.App.Dialogs;

public partial class TaskClassificationDialog : Window
{
    public TaskClassificationDialog(
        string documentName, 
        IEnumerable<string> recentCustomers,
        TaskClassificationSuggestion? aiSuggestion = null)
    {
        InitializeComponent();
        TaskNameText.Text = documentName;
        DocumentName = documentName;
        
        // Ensure the dialog comes to front when shown
        Loaded += OnLoaded;
        
        // Populate customer dropdown with recent customers
        foreach (var customer in recentCustomers)
        {
            CustomerCombo.Items.Add(customer);
        }

        // Apply AI suggestion if provided
        if (aiSuggestion is not null)
        {
            ApplyAiSuggestion(aiSuggestion);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Force the window to the foreground
        Activate();
        Topmost = true;
        Focus();
        
        // Briefly toggle Topmost to ensure it appears above other windows
        // then keep it topmost
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Topmost = false;
            Topmost = true;
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public string DocumentName { get; }
    public bool IsBillable => IsBillableCheckbox.IsChecked ?? true;
    public string? Category => string.IsNullOrWhiteSpace(CategoryCombo.Text) ? null : CategoryCombo.Text.Trim();
    public string? Customer => string.IsNullOrWhiteSpace(CustomerCombo.Text) ? null : CustomerCombo.Text.Trim();
    public string? Description => string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim();
    public bool WasSkipped { get; private set; }

    private void ApplyAiSuggestion(TaskClassificationSuggestion suggestion)
    {
        // Show the AI suggestion banner
        AiSuggestionBanner.Visibility = Visibility.Visible;
        
        // Set the reasoning text
        if (!string.IsNullOrWhiteSpace(suggestion.Reasoning))
        {
            AiReasoningText.Text = suggestion.Reasoning;
        }
        else
        {
            AiReasoningText.Text = "AI has analyzed your work context and pre-filled suggestions below.";
        }

        // Pre-fill billable checkbox
        IsBillableCheckbox.IsChecked = suggestion.IsBillable;

        // Pre-fill category if suggested
        if (!string.IsNullOrWhiteSpace(suggestion.Category))
        {
            // Check if category exists in combo box items
            bool found = false;
            for (int i = 0; i < CategoryCombo.Items.Count; i++)
            {
                if (CategoryCombo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), suggestion.Category, System.StringComparison.OrdinalIgnoreCase))
                {
                    CategoryCombo.SelectedIndex = i;
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                // Add and select the suggested category
                CategoryCombo.Text = suggestion.Category;
            }
        }

        // Pre-fill customer if detected
        if (!string.IsNullOrWhiteSpace(suggestion.CustomerName))
        {
            // Check if customer exists in recent customers
            bool found = false;
            for (int i = 0; i < CustomerCombo.Items.Count; i++)
            {
                if (string.Equals(CustomerCombo.Items[i]?.ToString(), suggestion.CustomerName, System.StringComparison.OrdinalIgnoreCase))
                {
                    CustomerCombo.SelectedIndex = i;
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                // Set the text for the new customer
                CustomerCombo.Text = suggestion.CustomerName;
            }
        }

        // Pre-fill description if suggested
        if (!string.IsNullOrWhiteSpace(suggestion.Description))
        {
            DescriptionTextBox.Text = suggestion.Description;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        WasSkipped = false;
        DialogResult = true;
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        WasSkipped = true;
        DialogResult = true;
        Close();
    }
}

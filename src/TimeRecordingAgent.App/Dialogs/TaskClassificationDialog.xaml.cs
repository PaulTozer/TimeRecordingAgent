using System.Collections.Generic;
using System.Windows;

namespace TimeRecordingAgent.App.Dialogs;

public partial class TaskClassificationDialog : Window
{
    public TaskClassificationDialog(string documentName, IEnumerable<string> recentCustomers)
    {
        InitializeComponent();
        TaskNameText.Text = documentName;
        DocumentName = documentName;
        
        // Populate customer dropdown with recent customers
        foreach (var customer in recentCustomers)
        {
            CustomerCombo.Items.Add(customer);
        }
    }

    public string DocumentName { get; }
    public bool IsBillable => IsBillableCheckbox.IsChecked ?? true;
    public string? Category => string.IsNullOrWhiteSpace(CategoryCombo.Text) ? null : CategoryCombo.Text.Trim();
    public string? Customer => string.IsNullOrWhiteSpace(CustomerCombo.Text) ? null : CustomerCombo.Text.Trim();
    public bool WasSkipped { get; private set; }

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

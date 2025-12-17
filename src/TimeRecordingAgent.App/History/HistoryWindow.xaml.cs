using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using TimeRecordingAgent.Core.Services;

namespace TimeRecordingAgent.App.History;

public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    private readonly RecordingCoordinator _coordinator;
    private readonly ILogger? _logger;
    private bool _isRefreshing;
    private bool _showOnlyUnapproved;
    private string _groupNameInput = string.Empty;
    private string _billableCategoryInput = string.Empty;
    private string _lastRefreshedText = "Loading...";
    private string _filterText = string.Empty;
    private ICollectionView? _entriesView;

    public HistoryWindow(RecordingCoordinator coordinator, ILogger? logger = null)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _logger = logger;
        DataContext = this;
        
        InitializeColumnConfigs();
        BuildDataGridColumns();
        
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void InitializeColumnConfigs()
    {
        ColumnConfigs.Add(new ColumnConfig("Approved", "IsApproved", true, 90));
        ColumnConfigs.Add(new ColumnConfig("Billable", "BillableDisplay", true, 90));
        ColumnConfigs.Add(new ColumnConfig("Category", "DisplayBillableCategory", true, 100));
        ColumnConfigs.Add(new ColumnConfig("Customer", "DisplayGroup", true, 120));
        ColumnConfigs.Add(new ColumnConfig("Document", "DocumentName", true, 200));
        ColumnConfigs.Add(new ColumnConfig("Process", "ProcessName", false, 150)); // Hidden by default
        ColumnConfigs.Add(new ColumnConfig("Window Title", "WindowTitle", true, 250));
        ColumnConfigs.Add(new ColumnConfig("Start", "StartedLocal", true, 140));
        ColumnConfigs.Add(new ColumnConfig("End", "EndedLocal", true, 140));
        ColumnConfigs.Add(new ColumnConfig("Duration", "FormattedDuration", true, 100));
        
        foreach (var config in ColumnConfigs)
        {
            config.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ColumnConfig.IsVisible))
                    BuildDataGridColumns();
                if (e.PropertyName == nameof(ColumnConfig.FilterText))
                    ApplyFilter();
            };
        }
    }

    private void BuildDataGridColumns()
    {
        HistoryGrid.Columns.Clear();
        
        foreach (var config in ColumnConfigs.Where(c => c.IsVisible))
        {
            DataGridColumn column;
            if (config.BindingPath == "IsApproved")
            {
                column = new DataGridCheckBoxColumn
                {
                    Header = config.Name,
                    Binding = new System.Windows.Data.Binding(config.BindingPath) { Mode = BindingMode.OneWay },
                    Width = new DataGridLength(config.Width)
                };
            }
            else if (config.BindingPath == "StartedLocal" || config.BindingPath == "EndedLocal")
            {
                column = new DataGridTextColumn
                {
                    Header = config.Name,
                    Binding = new System.Windows.Data.Binding(config.BindingPath) { StringFormat = "G" },
                    Width = new DataGridLength(config.Width)
                };
            }
            else if (config.BindingPath == "WindowTitle")
            {
                column = new DataGridTextColumn
                {
                    Header = config.Name,
                    Binding = new System.Windows.Data.Binding(config.BindingPath),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                };
            }
            else if (config.BindingPath == "FormattedDuration")
            {
                column = new DataGridTextColumn
                {
                    Header = config.Name,
                    Binding = new System.Windows.Data.Binding(config.BindingPath),
                    Width = new DataGridLength(config.Width),
                    SortMemberPath = "DurationSeconds"
                };
            }
            else
            {
                column = new DataGridTextColumn
                {
                    Header = config.Name,
                    Binding = new System.Windows.Data.Binding(config.BindingPath),
                    Width = new DataGridLength(config.Width)
                };
            }
            
            HistoryGrid.Columns.Add(column);
        }
    }

    public ObservableCollection<ColumnConfig> ColumnConfigs { get; } = new();

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = new();
    public ObservableCollection<GroupSummaryViewModel> GroupSummaries { get; } = new();
    public ObservableCollection<EmailSummaryViewModel> EmailSummaries { get; } = new();

    public bool ShowOnlyUnapproved
    {
        get => _showOnlyUnapproved;
        set
        {
            if (_showOnlyUnapproved == value)
            {
                return;
            }

            _showOnlyUnapproved = value;
            OnPropertyChanged();
            _ = RefreshAsync();
        }
    }

    public string GroupNameInput
    {
        get => _groupNameInput;
        set
        {
            if (_groupNameInput == value)
            {
                return;
            }

            _groupNameInput = value;
            OnPropertyChanged();
        }
    }

    public string BillableCategoryInput
    {
        get => _billableCategoryInput;
        set
        {
            if (_billableCategoryInput == value)
            {
                return;
            }

            _billableCategoryInput = value;
            OnPropertyChanged();
        }
    }

    public string LastRefreshedText
    {
        get => _lastRefreshedText;
        private set
        {
            if (_lastRefreshedText == value)
            {
                return;
            }

            _lastRefreshedText = value;
            OnPropertyChanged();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        _entriesView?.Refresh();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not HistoryEntryViewModel entry) return false;
        
        // Global text filter
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.Trim();
            var matchesGlobal = 
                (entry.DocumentName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.ProcessName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.WindowTitle?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (entry.DisplayGroup?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
            
            if (!matchesGlobal) return false;
        }
        
        // Per-column filters
        foreach (var config in ColumnConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.FilterText)) continue;
            
            var colFilter = config.FilterText.Trim();
            var value = config.BindingPath switch
            {
                "DocumentName" => entry.DocumentName,
                "ProcessName" => entry.ProcessName,
                "WindowTitle" => entry.WindowTitle,
                "DisplayGroup" => entry.DisplayGroup,
                _ => null
            };
            
            if (value is not null && !value.Contains(colFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            await Task.Run(() => _coordinator.FlushCurrentSample()).ConfigureAwait(true);
            var records = await Task.Run(() => _coordinator.GetRecentSamples(500)).ConfigureAwait(true);
            var filtered = (ShowOnlyUnapproved ? records.Where(r => !r.IsApproved) : records).ToList();

            var liveRecord = _coordinator.GetCurrentActivity();
            if (liveRecord is not null && (!ShowOnlyUnapproved || !liveRecord.IsApproved))
            {
                filtered.Insert(0, liveRecord);
            }

            var entryModels = filtered.Select(r => new HistoryEntryViewModel(r, r.Id < 0)).ToList();
            var groupModels = filtered
                .GroupBy(r => string.IsNullOrWhiteSpace(r.GroupName) ? "(No Customer)" : r.GroupName!)
                .Select(g => new GroupSummaryViewModel(g.Key, TimeSpan.FromSeconds(g.Sum(r => r.Duration.TotalSeconds))))
                .OrderByDescending(g => g.TotalMinutes)
                .ToList();

            Entries.Clear();
            foreach (var entry in entryModels)
            {
                Entries.Add(entry);
            }

            _entriesView = CollectionViewSource.GetDefaultView(Entries);
            _entriesView.Filter = FilterEntry;

            GroupSummaries.Clear();
            foreach (var summary in groupModels)
            {
                GroupSummaries.Add(summary);
            }

            // Calculate email time summaries (Outlook entries only)
            var emailModels = filtered
                .Where(r => r.ProcessName.Equals("OUTLOOK", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(r.DocumentName)
                         && !r.DocumentName.Contains(" - ") // Exclude "Inbox - user@email.com" style entries
                         && !r.DocumentName.StartsWith("Inbox", StringComparison.OrdinalIgnoreCase)
                         && !r.DocumentName.StartsWith("Calendar", StringComparison.OrdinalIgnoreCase)
                         && !r.DocumentName.StartsWith("Sent Items", StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.DocumentName)
                .Select(g => new EmailSummaryViewModel(
                    g.Key,
                    TimeSpan.FromSeconds(g.Sum(r => r.Duration.TotalSeconds)),
                    g.Count()))
                .OrderByDescending(e => e.TotalMinutes)
                .Take(20) // Limit to top 20 emails
                .ToList();

            EmailSummaries.Clear();
            foreach (var email in emailModels)
            {
                EmailSummaries.Add(email);
            }

            LastRefreshedText = $"Last refreshed {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh history window");
            System.Windows.MessageBox.Show(this, "Unable to load history. Check logs for details.", "History", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private List<long> GetSelectedIds()
    {
        return HistoryGrid.SelectedItems.OfType<HistoryEntryViewModel>()
            .Select(entry => entry.Id)
            .ToList();
    }

    private async void ApproveSelected_Click(object sender, RoutedEventArgs e)
    {
        await UpdateApprovalAsync(true);
    }

    private async void MarkPending_Click(object sender, RoutedEventArgs e)
    {
        await UpdateApprovalAsync(false);
    }

    private async Task UpdateApprovalAsync(bool isApproved)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await Task.Run(() => _coordinator.SetApproval(ids, isApproved));
        await RefreshAsync();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(this, $"Delete {ids.Count} selected entries?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await Task.Run(() => _coordinator.DeleteSamples(ids));
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void AssignGroup_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var group = string.IsNullOrWhiteSpace(GroupNameInput) ? null : GroupNameInput.Trim();
        await Task.Run(() => _coordinator.SetGroupName(ids, group));
        await RefreshAsync();
    }

    private async void ClearGroup_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await Task.Run(() => _coordinator.SetGroupName(ids, null));
        await RefreshAsync();
    }

    private async void MarkBillable_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await Task.Run(() => _coordinator.SetBillable(ids, true));
        await RefreshAsync();
    }

    private async void MarkNonBillable_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        await Task.Run(() => _coordinator.SetBillable(ids, false));
        await RefreshAsync();
    }

    private async void AssignCategory_Click(object sender, RoutedEventArgs e)
    {
        var ids = GetSelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var category = string.IsNullOrWhiteSpace(BillableCategoryInput) ? null : BillableCategoryInput.Trim();
        await Task.Run(() => _coordinator.SetBillableCategory(ids, category));
        await RefreshAsync();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

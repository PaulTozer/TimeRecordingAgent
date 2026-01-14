using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TimeRecordingAgent.App.Dialogs;
using TimeRecordingAgent.App.History;
using TimeRecordingAgent.Core.Configuration;
using TimeRecordingAgent.Core.Models;
using TimeRecordingAgent.Core.Services;
using WpfApplication = System.Windows.Application;

namespace TimeRecordingAgent.App.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly RecordingCoordinator _coordinator;
    private readonly AiClassificationService _aiService;
    private readonly IOutlookContextReader _outlookContextReader;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly ILogger<TrayIconManager> _logger;
    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _promptToggleItem;
    private readonly ToolStripMenuItem _aiToggleItem;
    private readonly ToolStripMenuItem _aiSettingsItem;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _taskPromptTimer;
    private readonly HashSet<string> _promptedDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _promptThreshold = TimeSpan.FromMinutes(5);
    private AppSettings _settings;
    private HistoryWindow? _historyWindow;
    private Icon? _clockIcon;
    private bool _disposed;
    private string? _currentTrackedDocument;
    private string? _currentTrackedProcess;
    private string? _currentTrackedWindowTitle;

    public TrayIconManager(
        RecordingCoordinator coordinator, 
        AiClassificationService aiService, 
        IOutlookContextReader outlookContextReader,
        ScreenCaptureService screenCaptureService,
        ILogger<TrayIconManager> logger, 
        string databasePath)
    {
        _coordinator = coordinator;
        _aiService = aiService;
        _outlookContextReader = outlookContextReader;
        _screenCaptureService = screenCaptureService;
        _logger = logger;
        _dataDirectory = Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(_dataDirectory);
        
        // Load settings from configuration file
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _settings = AppSettings.Load(_settingsPath);
        _logger.LogInformation("Loaded settings from {Path}", _settingsPath);
        
        // Configure AI service from loaded settings
        _aiService.Configure(_settings.AzureAi);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            Text = "Time Recording Agent",
        };

        _toggleItem = new ToolStripMenuItem("Pause Recording", null, (_, _) => ToggleRecording());
        _promptToggleItem = new ToolStripMenuItem("Task Prompts Enabled", null, (_, _) => ToggleTaskPrompts())
        {
            Checked = _settings.General.TaskPromptsEnabled
        };
        _aiToggleItem = new ToolStripMenuItem("AI Suggestions Enabled", null, (_, _) => ToggleAiSuggestions())
        {
            Checked = _settings.AzureAi.Enabled && _aiService.IsConfigured,
            Enabled = _aiService.IsConfigured
        };
        _aiSettingsItem = new ToolStripMenuItem("Configure AI...", null, (_, _) => ShowAiSettingsDialog());
        
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _clockTimer.Tick += HandleClockTick;
        
        _taskPromptTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _taskPromptTimer.Tick += HandleTaskPromptTick;
        
        if (!_aiService.IsConfigured)
        {
            _logger.LogInformation("AI suggestions are not configured. Use 'Configure AI...' from the tray menu to set up.");
        }
    }

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_promptToggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_aiToggleItem);
        menu.Items.Add(_aiSettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Export Today", null, async (_, _) => await ExportTodayAsync()));
        menu.Items.Add(new ToolStripMenuItem("Open Log Folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += HandleNotifyIconClick;
        UpdateClockIcon();
        _clockTimer.Start();
        _taskPromptTimer.Start();

        _coordinator.ContextChanged += HandleContextChanged;
        _coordinator.SampleStored += HandleSampleStored;
        _coordinator.Start();
        UpdateTooltip(_coordinator.CurrentContext);
    }

    private void ShowAiSettingsDialog()
    {
        WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var dialog = new AiSettingsDialog(_settings.AzureAi);
                var result = dialog.ShowDialog();

                if (result == true && dialog.WasSaved)
                {
                    // Update settings
                    _settings.AzureAi = dialog.UpdatedSettings;
                    _settings.Save(_settingsPath);
                    _logger.LogInformation("AI settings saved to {Path}", _settingsPath);

                    // Reconfigure AI service
                    _aiService.Configure(_settings.AzureAi);

                    // Update menu items
                    _aiToggleItem.Checked = _settings.AzureAi.Enabled && _aiService.IsConfigured;
                    _aiToggleItem.Enabled = _aiService.IsConfigured;

                    if (_aiService.IsEnabled)
                    {
                        _logger.LogInformation("AI classification is now active.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show AI settings dialog");
            }
        });
    }

    private void ToggleAiSuggestions()
    {
        if (!_aiService.IsConfigured)
        {
            // If not configured, open settings dialog instead
            ShowAiSettingsDialog();
            return;
        }

        _settings.AzureAi.Enabled = !_settings.AzureAi.Enabled;
        _aiToggleItem.Checked = _settings.AzureAi.Enabled;
        _aiService.SetEnabled(_settings.AzureAi.Enabled);
        _settings.Save(_settingsPath);
        _logger.LogInformation("AI suggestions {State}.", _settings.AzureAi.Enabled ? "enabled" : "disabled");
    }

    private void ToggleTaskPrompts()
    {
        _settings.General.TaskPromptsEnabled = !_settings.General.TaskPromptsEnabled;
        _promptToggleItem.Checked = _settings.General.TaskPromptsEnabled;
        _settings.Save(_settingsPath);
        _logger.LogInformation("Task prompts {State}.", _settings.General.TaskPromptsEnabled ? "enabled" : "disabled");
    }

    private void HandleTaskPromptTick(object? sender, EventArgs e)
    {
        if (!_settings.General.TaskPromptsEnabled || !_coordinator.IsRunning)
        {
            return;
        }

        CheckAndPromptForTask();
    }

    private void TrackCurrentDocument(ActiveContextSnapshot? snapshot)
    {
        var newDocument = snapshot?.DocumentName;
        
        if (string.Equals(_currentTrackedDocument, newDocument, StringComparison.OrdinalIgnoreCase))
        {
            // Update window title even if document hasn't changed (for AI context)
            _currentTrackedWindowTitle = snapshot?.WindowTitle;
            return;
        }

        _currentTrackedDocument = newDocument;
        _currentTrackedProcess = snapshot?.ProcessName;
        _currentTrackedWindowTitle = snapshot?.WindowTitle;
        _logger.LogTrace("Now tracking document: {Document}", newDocument ?? "(none)");
    }

    private void CheckAndPromptForTask()
    {
        if (_currentTrackedDocument == null)
        {
            return;
        }

        // Skip if already prompted for this document today
        if (_promptedDocuments.Contains(_currentTrackedDocument))
        {
            return;
        }

        // Check accumulated time from the database instead of continuous time
        var accumulatedTime = GetAccumulatedTimeToday(_currentTrackedDocument);
        if (accumulatedTime < _promptThreshold)
        {
            return;
        }

        _logger.LogDebug("Prompting for task classification: {Document} (accumulated {Accumulated})", 
            _currentTrackedDocument, accumulatedTime);

        // Mark as prompted to prevent duplicate prompts
        _promptedDocuments.Add(_currentTrackedDocument);

        // Capture current context for the async operation
        var documentName = _currentTrackedDocument;
        var processName = _currentTrackedProcess ?? "Unknown";
        var windowTitle = _currentTrackedWindowTitle ?? documentName;

        WpfApplication.Current.Dispatcher.Invoke(() => 
            ShowTaskClassificationDialogAsync(documentName, processName, windowTitle));
    }

    private TimeSpan GetAccumulatedTimeToday(string documentName)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var records = _coordinator.GetRecentSamples(500)
                .Where(r => string.Equals(r.DocumentName, documentName, StringComparison.OrdinalIgnoreCase)
                         && DateOnly.FromDateTime(r.StartedAtLocal) == today)
                .ToList();

            var totalSeconds = records.Sum(r => r.Duration.TotalSeconds);
            return TimeSpan.FromSeconds(totalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get accumulated time for {Document}", documentName);
            return TimeSpan.Zero;
        }
    }

    private async void ShowTaskClassificationDialogAsync(string documentName, string processName, string windowTitle)
    {
        try
        {
            // Get recent customers for the dropdown
            var recentCustomers = _coordinator.GetRecentSamples(100)
                .Where(r => !string.IsNullOrWhiteSpace(r.GroupName))
                .Select(r => r.GroupName!)
                .Distinct()
                .Take(10)
                .ToList();

            // Get AI suggestion if enabled
            TaskClassificationSuggestion? aiSuggestion = null;
            if (_aiService.IsEnabled)
            {
                _logger.LogDebug("Requesting AI classification for: {Document}", documentName);
                try
                {
                    // Try to get content snippet from Outlook for emails/meetings
                    string? contentSnippet = null;
                    var processLower = processName.ToLowerInvariant();
                    bool isOutlook = processLower.Contains("outlook") || processLower is "olk" or "hxoutlook" or "hxmail";
                    
                    if (isOutlook)
                    {
                        var outlookContext = _outlookContextReader.TryGetActiveItemContext(windowTitle);
                        if (outlookContext?.BodySnippet != null)
                        {
                            contentSnippet = outlookContext.BodySnippet;
                            _logger.LogDebug("Got content snippet from Outlook ({Type}): {Length} chars", 
                                outlookContext.ItemType, contentSnippet.Length);
                        }
                    }

                    // If no text content available, try vision-based classification
                    if (string.IsNullOrWhiteSpace(contentSnippet))
                    {
                        // Use screen capture for Teams, browsers, and other apps
                        var screenshot = _screenCaptureService.CaptureActiveWindowAsBase64();
                        if (!string.IsNullOrEmpty(screenshot))
                        {
                            _logger.LogDebug("Using vision-based classification for {Process}", processName);
                            aiSuggestion = await _aiService.ClassifyTaskWithScreenshotAsync(
                                processName,
                                windowTitle,
                                documentName,
                                screenshot,
                                recentCustomers);
                        }
                    }
                    
                    // Fall back to text-based classification if we have content or vision failed
                    if (aiSuggestion is null)
                    {
                        aiSuggestion = await _aiService.ClassifyTaskAsync(
                            processName,
                            windowTitle,
                            documentName,
                            contentSnippet,
                            recentCustomers);
                    }
                    
                    if (aiSuggestion is not null)
                    {
                        _logger.LogInformation(
                            "AI suggestion for '{Document}': Customer={Customer}, Category={Category}, Billable={Billable}, Description={Description}",
                            documentName,
                            aiSuggestion.CustomerName ?? "(none)",
                            aiSuggestion.Category,
                            aiSuggestion.IsBillable,
                            aiSuggestion.Description ?? "(none)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI classification failed, proceeding without suggestion");
                }
            }

            var dialog = new TaskClassificationDialog(documentName, recentCustomers, aiSuggestion);
            var result = dialog.ShowDialog();

            if (result != true || dialog.WasSkipped)
            {
                _logger.LogDebug("Task classification skipped for: {Document}", documentName);
                return;
            }

            // Apply the classification to matching records
            ApplyTaskClassification(documentName, dialog.IsBillable, dialog.Category, dialog.Customer, dialog.Description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show task classification dialog");
        }
    }

    private void ShowTaskClassificationDialog(string documentName)
    {
        // Legacy method - redirect to async version with current context
        var processName = _currentTrackedProcess ?? "Unknown";
        var windowTitle = _currentTrackedWindowTitle ?? documentName;
        ShowTaskClassificationDialogAsync(documentName, processName, windowTitle);
    }

    private void ApplyTaskClassification(string documentName, bool isBillable, string? category, string? customer, string? description)
    {
        try
        {
            // Get today's records matching this document
            var today = DateOnly.FromDateTime(DateTime.Now);
            var records = _coordinator.GetRecentSamples(500)
                .Where(r => string.Equals(r.DocumentName, documentName, StringComparison.OrdinalIgnoreCase)
                         && DateOnly.FromDateTime(r.StartedAtLocal) == today)
                .Select(r => r.Id)
                .ToList();

            if (records.Count == 0)
            {
                _logger.LogWarning("No records found to classify for document: {Document}", documentName);
                return;
            }

            _coordinator.SetBillable(records, isBillable);
            
            if (!string.IsNullOrWhiteSpace(category))
            {
                _coordinator.SetBillableCategory(records, category);
            }
            
            if (!string.IsNullOrWhiteSpace(customer))
            {
                _coordinator.SetGroupName(records, customer);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                _coordinator.SetDescription(records, description);
            }

            _logger.LogInformation("Classified {Count} records for '{Document}': Billable={Billable}, Category={Category}, Customer={Customer}, Description={Description}",
                records.Count, documentName, isBillable, category ?? "(none)", customer ?? "(none)", description ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply task classification for: {Document}", documentName);
        }
    }

    private void ToggleRecording()
    {
        if (_coordinator.IsRunning)
        {
            _coordinator.Pause();
            _toggleItem.Text = "Resume Recording";
            UpdateTooltip(null);
        }
        else
        {
            _coordinator.Start();
            _toggleItem.Text = "Pause Recording";
            UpdateTooltip(_coordinator.CurrentContext);
        }
    }

    private void HandleNotifyIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            if (_historyWindow is { IsVisible: true })
            {
                _historyWindow.Activate();
                return;
            }

            _historyWindow = new HistoryWindow(_coordinator, _logger);
            UpdateHistoryWindowIcon(_clockIcon);
            _historyWindow.Closed += HandleHistoryWindowClosed;
            _historyWindow.Show();
        });
    }

    private void HandleHistoryWindowClosed(object? sender, EventArgs e)
    {
        if (_historyWindow is not null)
        {
            _historyWindow.Closed -= HandleHistoryWindowClosed;
            _historyWindow = null;
        }
    }

    private async Task ExportTodayAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var summary = _coordinator.GetDailySummary(today);
            if (summary.Count == 0)
            {
                ShowBalloon("No entries for today yet.");
                return;
            }

            var exportDir = Path.Combine(_dataDirectory, "exports");
            Directory.CreateDirectory(exportDir);
            var filePath = Path.Combine(exportDir, $"time-{today:yyyyMMdd}.csv");
            await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            await writer.WriteLineAsync("Process,Document,Duration,TotalSeconds");
            foreach (var row in summary)
            {
                await writer.WriteLineAsync($"\"{row.ProcessName}\",\"{row.DocumentName}\",\"{row.FormattedDuration}\",{(int)row.Duration.TotalSeconds}");
            }

            ShowBalloon($"Saved {summary.Count} rows to {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export CSV");
            ShowBalloon("Export failed. Check logs.");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _dataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log folder");
        }
    }

    private void ExitApplication()
    {
        WpfApplication.Current.Dispatcher.Invoke(() => WpfApplication.Current.Shutdown());
    }

    private void HandleContextChanged(object? sender, ActiveContextSnapshot? snapshot)
    {
        WpfApplication.Current.Dispatcher.Invoke(() => 
        {
            UpdateTooltip(snapshot);
            TrackCurrentDocument(snapshot);
        });
    }

    private void HandleSampleStored(object? sender, ActivitySample sample)
    {
        _logger.LogDebug("Stored {Process} - {Document} for {Duration} sec", sample.ProcessName, sample.DocumentName, sample.Duration.TotalSeconds);
    }

    private void UpdateTooltip(ActiveContextSnapshot? snapshot)
    {
        string text;
        if (!_coordinator.IsRunning)
        {
            text = "Time Recording Agent (paused)";
        }
        else if (snapshot is null)
        {
            text = "Time Recording Agent (waiting for activity)";
        }
        else
        {
            text = $"{snapshot.DocumentName} ({snapshot.ProcessName})";
        }
        SetNotifyText(text);
    }

    private void ShowBalloon(string message)
    {
        _notifyIcon.BalloonTipTitle = "Time Recording Agent";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void SetNotifyText(string text)
    {
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void HandleClockTick(object? sender, EventArgs e)
    {
        UpdateClockIcon();
    }

    private void UpdateClockIcon()
    {
        try
        {
            const int size = 32;
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var center = new PointF(size / 2f, size / 2f);
            var radius = size / 2f - 2f;
            var now = DateTime.Now;

            using (var faceBrush = new SolidBrush(Color.FromArgb(250, 250, 250)))
            using (var borderPen = new Pen(Color.FromArgb(80, 80, 80), 2f))
            {
                graphics.FillEllipse(faceBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                graphics.DrawEllipse(borderPen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }

            using (var tickPen = new Pen(Color.FromArgb(150, 150, 150), 1f))
            {
                for (var i = 0; i < 12; i++)
                {
                    var angle = i * 30 - 90;
                    var inner = CalculatePoint(center, radius - 4, angle);
                    var outer = CalculatePoint(center, radius - 1, angle);
                    graphics.DrawLine(tickPen, inner, outer);
                }
            }

            using (var hourPen = new Pen(Color.FromArgb(60, 60, 60), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var minutePen = new Pen(Color.FromArgb(30, 30, 30), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var secondPen = new Pen(Color.FromArgb(200, 60, 60), 1f))
            {
                var hourAngle = ((now.Hour % 12) + now.Minute / 60f) * 30 - 90;
                var minuteAngle = now.Minute * 6 - 90;
                var secondAngle = now.Second * 6 - 90;

                DrawHand(graphics, center, radius * 0.55f, hourAngle, hourPen);
                DrawHand(graphics, center, radius * 0.8f, minuteAngle, minutePen);
                DrawHand(graphics, center, radius * 0.85f, secondAngle, secondPen);
            }

            var handle = bitmap.GetHicon();
            using var tempIcon = Icon.FromHandle(handle);
            var clone = (Icon)tempIcon.Clone();
            DestroyIcon(handle);
            SetNotifyIcon(clone);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update tray clock icon");
        }
    }

    private void SetNotifyIcon(Icon icon)
    {
        var previous = _clockIcon;
        _clockIcon = icon;
        _notifyIcon.Icon = icon;
        UpdateHistoryWindowIcon(icon);
        previous?.Dispose();
    }

    private void UpdateHistoryWindowIcon(Icon? icon)
    {
        if (_historyWindow is null || icon is null)
        {
            return;
        }

        void ApplyIcon()
        {
            var handle = icon.Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(icon.Width, icon.Height));
            image.Freeze();
            _historyWindow.Icon = image;
        }

        var dispatcher = WpfApplication.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            ApplyIcon();
        }
        else
        {
            dispatcher.Invoke(ApplyIcon);
        }
    }

    private static void DrawHand(Graphics graphics, PointF center, float length, double angleDegrees, Pen pen)
    {
        var end = CalculatePoint(center, length, angleDegrees);
        graphics.DrawLine(pen, center, end);
    }

    private static PointF CalculatePoint(PointF center, float length, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new PointF(
            center.X + (float)(Math.Cos(radians) * length),
            center.Y + (float)(Math.Sin(radians) * length));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _clockTimer.Tick -= HandleClockTick;
        _clockTimer.Stop();
        _taskPromptTimer.Tick -= HandleTaskPromptTick;
        _taskPromptTimer.Stop();
        _coordinator.ContextChanged -= HandleContextChanged;
        _coordinator.SampleStored -= HandleSampleStored;
        _notifyIcon.MouseClick -= HandleNotifyIconClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _clockIcon?.Dispose();
        _notifyIcon.Dispose();
        if (_historyWindow is not null)
        {
            var window = _historyWindow;
            _historyWindow = null;
            window.Closed -= HandleHistoryWindowClosed;
            window.Close();
        }
        _disposed = true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

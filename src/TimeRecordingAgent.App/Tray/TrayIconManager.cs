using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TimeRecordingAgent.App.History;
using TimeRecordingAgent.Core.Models;
using TimeRecordingAgent.Core.Services;
using WpfApplication = System.Windows.Application;

namespace TimeRecordingAgent.App.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly RecordingCoordinator _coordinator;
    private readonly ILogger<TrayIconManager> _logger;
    private readonly string _dataDirectory;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly DispatcherTimer _clockTimer;
    private HistoryWindow? _historyWindow;
    private Icon? _clockIcon;
    private bool _disposed;

    public TrayIconManager(RecordingCoordinator coordinator, ILogger<TrayIconManager> logger, string databasePath)
    {
        _coordinator = coordinator;
        _logger = logger;
        _dataDirectory = Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(_dataDirectory);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            Text = "Time Recording Agent",
        };

        _toggleItem = new ToolStripMenuItem("Pause Recording", null, (_, _) => ToggleRecording());
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _clockTimer.Tick += HandleClockTick;
    }

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripMenuItem("Export Today", null, async (_, _) => await ExportTodayAsync()));
        menu.Items.Add(new ToolStripMenuItem("Open Log Folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += HandleNotifyIconClick;
        UpdateClockIcon();
        _clockTimer.Start();

        _coordinator.ContextChanged += HandleContextChanged;
        _coordinator.SampleStored += HandleSampleStored;
        _coordinator.Start();
        UpdateTooltip(_coordinator.CurrentContext);
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
        WpfApplication.Current.Dispatcher.Invoke(() => UpdateTooltip(snapshot));
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

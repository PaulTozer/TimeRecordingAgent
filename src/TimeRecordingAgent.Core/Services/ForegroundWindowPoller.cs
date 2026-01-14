using System.Collections.Generic;
using TimeRecordingAgent.Core.Models;
using Timer = System.Threading.Timer;

namespace TimeRecordingAgent.Core.Services;

public sealed class ForegroundWindowPoller : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly ScreenStateMonitor _screenState;
    private readonly IOutlookContextReader _outlookContextReader;
    private readonly ILogger<ForegroundWindowPoller> _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _excludedProcessNames;
    private readonly HashSet<string> _blankTitleAllowedProcesses;
    private readonly int _selfProcessId;
    private FocusBuffer? _current;
    private Timer? _timer;
    private bool _disposed;

    public ForegroundWindowPoller(
        TimeSpan interval,
        ScreenStateMonitor screenState,
        IOutlookContextReader outlookContextReader,
        ILogger<ForegroundWindowPoller> logger,
        IEnumerable<string>? excludedProcessNames = null)
    {
        _interval = interval;
        _screenState = screenState;
        _outlookContextReader = outlookContextReader;
        _logger = logger;
        _selfProcessId = Environment.ProcessId;
        _excludedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Process.GetCurrentProcess().ProcessName,
            "TimeRecordingAgent",
            "explorer",
            "explorer.exe",
            "ShellExperienceHost.exe",
            "ShellExperienceHost",
            "StartMenuExperienceHost.exe",
            "StartMenuExperienceHost",
            "RuntimeBroker.exe",
            "RuntimeBroker",
        };

        _blankTitleAllowedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OUTLOOK",
            "OLK",
            "HXOUTLOOK",
            "HXMAIL",
            "msedgewebview2", // Embedded web content in Office apps
        };

        if (excludedProcessNames is not null)
        {
            foreach (var name in excludedProcessNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _excludedProcessNames.Add(name);
                }
            }
        }
    }

    public event EventHandler<ActivitySample>? SampleFinalized;
    public event EventHandler<ActiveContextSnapshot?>? ContextChanged;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new Timer(Poll, null, TimeSpan.Zero, _interval);
        _logger.LogInformation("Foreground polling started with interval {Interval}.", _interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        FlushCurrent(DateTime.UtcNow, "poller stopped");
        _logger.LogInformation("Foreground polling stopped.");
    }

    public void FlushActive()
    {
        _logger.LogDebug("Manual flush requested by coordinator.");
        FlushCurrent(DateTime.UtcNow, "manual flush");
    }

    private void Poll(object? _)
    {
        try
        {
            _screenState.Refresh();
            var now = DateTime.UtcNow;

            if (_screenState.IsScreenUnavailable)
            {
                _logger.LogTrace("Screen unavailable (locked or display off); flushing focus buffer.");
                FlushCurrent(now, "screen unavailable");
                return;
            }

            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero || handle == NativeMethods.GetShellWindow())
            {
                _logger.LogTrace("No actionable foreground window (handle {Handle}).", handle);
                FlushCurrent(now, "no foreground window");
                return;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            Process? process = null;
            try
            {
                process = Process.GetProcessById(unchecked((int)pid));
                _logger.LogTrace("Foreground window {Handle} belongs to process {ProcessName} (PID {Pid}).", handle, process.ProcessName, pid);
            }
            catch (ArgumentException ex)
            {
                _logger.LogDebug(ex, "Process {Pid} disappeared before it could be inspected.", pid);
                FlushCurrent(now, "process not found");
                return;
            }

            var title = NativeMethods.GetWindowTextSafe(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                _logger.LogTrace("Primary window title blank for {Process}; attempting fallback.", process.ProcessName);
                var fallback = SafeGetMainWindowTitle(process);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    title = fallback;
                    _logger.LogTrace("Fallback main window title '{Title}' used for {Process}.", title, process.ProcessName);
                }
            }

            var effectiveProcessName = DeriveProcessAlias(handle, process, ref title);
            if (!string.Equals(effectiveProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Process {Original} remapped to alias {Alias}.", process.ProcessName, effectiveProcessName);
            }

            if (ShouldExcludeProcess(process))
            {
                _logger.LogTrace("Excluded process {Process}; current focus buffer flushed.", process.ProcessName);
                FlushCurrent(now, "excluded process");
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                if (_blankTitleAllowedProcesses.Contains(effectiveProcessName))
                {
                    title = effectiveProcessName;
                    _logger.LogTrace("Blank title allowed for {Process}; substituting process name.", effectiveProcessName);
                }
                else if (!TryCaptureOutlookChildTitle(handle, ref title))
                {
                    _logger.LogTrace("Unable to capture non-empty title for {Process}; flushing focus buffer.", effectiveProcessName);
                    FlushCurrent(now, "blank title");
                    return;
                }
                else
                {
                    _logger.LogTrace("Captured child window title '{Title}' for {Process}.", title, effectiveProcessName);
                }
            }

            title ??= effectiveProcessName;
            var documentName = WindowContextResolver.ResolveDocumentName(effectiveProcessName, title, wt => _outlookContextReader.TryGetActiveSubject(wt));
            var snapshot = new ActiveContextSnapshot(handle, effectiveProcessName, title, documentName, now);
            _logger.LogDebug("Observed context {Process} | '{Title}' | '{Document}'.", effectiveProcessName, title, documentName ?? "<none>");

            ActivitySample? sampleToEmit = null;
            ActiveContextSnapshot? raisedSnapshot = null;
            lock (_gate)
            {
                if (_current is null)
                {
                    _current = new FocusBuffer(snapshot);
                    raisedSnapshot = snapshot;
                    _logger.LogTrace("Started new focus buffer for {Process}.", snapshot.ProcessName);
                }
                else if (_current.IsSameContext(snapshot))
                {
                    _current.Touch(now);
                    _logger.LogTrace("Extended focus buffer for {Process} (duration now ~{DurationSeconds:F1}s).", snapshot.ProcessName, (now - _current.Snapshot.StartedAtUtc).TotalSeconds);
                }
                else
                {
                    sampleToEmit = CreateSample(_current, now);
                    _current = new FocusBuffer(snapshot);
                    raisedSnapshot = snapshot;
                    _logger.LogTrace("Switch detected: emitting previous sample and starting buffer for {Process}.", snapshot.ProcessName);
                }
            }

            if (sampleToEmit is not null)
            {
                _logger.LogDebug("Emitting sample for {Process} lasting {DurationSeconds:F1}s.", sampleToEmit.ProcessName, sampleToEmit.Duration.TotalSeconds);
                SampleFinalized?.Invoke(this, sampleToEmit);
            }

            if (raisedSnapshot is not null)
            {
                _logger.LogTrace("Context change raised for {Process}.", raisedSnapshot.ProcessName);
                ContextChanged?.Invoke(this, raisedSnapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Foreground poll failed");
        }
    }

    private static string? SafeGetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string DeriveProcessAlias(IntPtr handle, Process process, ref string? title)
    {
        var name = process.ProcessName;
        
        // Handle WebView2-hosted content (used by Outlook, PowerPoint embeds, etc.)
        if (string.Equals(name, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
        {
            // Try to identify the host application from window title or parent
            if (ContainsOutlookHint(title))
            {
                return "OUTLOOK";
            }
            
            // For embedded Power BI or other WebView2 content, try to get meaningful context
            // Check if title suggests PowerPoint embedded content
            if (!string.IsNullOrWhiteSpace(title) && 
                (title.Contains("Power BI", StringComparison.OrdinalIgnoreCase) ||
                 title.Contains("PowerBI", StringComparison.OrdinalIgnoreCase)))
            {
                return "POWERPNT"; // Attribute to PowerPoint
            }
            
            // Keep as msedgewebview2 but it will be captured (not discarded)
            return "msedgewebview2";
        }
        
        if (IsModernOutlookHost(name))
        {
            if (ContainsOutlookHint(title) || TryCaptureOutlookChildTitle(handle, ref title))
            {
                return "OUTLOOK";
            }
        }

        return name;
    }

    private static bool TryCaptureOutlookChildTitle(IntPtr handle, ref string? title)
    {
        string? found = null;
        NativeMethods.EnumChildWindows(handle, (child, _) =>
        {
            var text = NativeMethods.GetWindowTextSafe(child);
            if (ContainsOutlookHint(text))
            {
                found = text;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (!string.IsNullOrWhiteSpace(found))
        {
            title = found;
            return true;
        }

        return false;
    }

    private static bool ContainsOutlookHint(string? text)
    {
        return !string.IsNullOrWhiteSpace(text)
               && text.IndexOf("outlook", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsModernOutlookHost(string processName)
    {
        return string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(processName, "msedgewebview2", StringComparison.OrdinalIgnoreCase)
               || string.Equals(processName, "olk", StringComparison.OrdinalIgnoreCase)
               || string.Equals(processName, "newoutlook", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldExcludeProcess(Process process)
    {
        if (process.Id == _selfProcessId)
        {
            return true;
        }

        return _excludedProcessNames.Contains(process.ProcessName);
    }

    private ActivitySample? CreateSample(FocusBuffer buffer, DateTime now)
    {
        var sample = buffer.ToSample(now);
        if (sample.Duration < TimeSpan.FromSeconds(2))
        {
            _logger.LogTrace("Discarding sub-threshold sample for {Process} lasting {DurationSeconds:F1}s.", sample.ProcessName, sample.Duration.TotalSeconds);
            return null;
        }

        return sample;
    }

    private void FlushCurrent(DateTime now, string reason)
    {
        ActivitySample? sampleToEmit = null;
        var shouldRaise = false;
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            sampleToEmit = CreateSample(_current, now);
            _current = null;
            shouldRaise = true;
        }

        _logger.LogTrace("Flushed current focus buffer due to {Reason}.", reason);

        if (sampleToEmit is not null)
        {
            _logger.LogDebug("Emitting flushed sample for {Process} lasting {DurationSeconds:F1}s.", sampleToEmit.ProcessName, sampleToEmit.Duration.TotalSeconds);
            SampleFinalized?.Invoke(this, sampleToEmit);
        }

        if (shouldRaise)
        {
            ContextChanged?.Invoke(this, null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _screenState.Dispose();
        _disposed = true;
    }

    private sealed class FocusBuffer
    {
        private DateTime _lastSeenUtc;
        internal FocusBuffer(ActiveContextSnapshot snapshot)
        {
            Snapshot = snapshot;
            _lastSeenUtc = snapshot.StartedAtUtc;
        }

        internal ActiveContextSnapshot Snapshot { get; }

        internal void Touch(DateTime now)
        {
            _lastSeenUtc = now;
        }

        internal bool IsSameContext(ActiveContextSnapshot other)
        {
            return Snapshot.WindowHandle == other.WindowHandle
                   && string.Equals(Snapshot.DocumentName, other.DocumentName, StringComparison.Ordinal)
                   && string.Equals(Snapshot.ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        internal ActivitySample ToSample(DateTime now)
        {
            // Always use 'now' as the end time - this is when we detected the context change
            // or when a flush was requested. Using _lastSeenUtc would lose time between
            // the last poll that saw this context and when the switch was detected.
            return new ActivitySample(
                Snapshot.StartedAtUtc,
                now,
                Snapshot.ProcessName,
                Snapshot.WindowTitle,
                Snapshot.DocumentName);
        }
    }
}

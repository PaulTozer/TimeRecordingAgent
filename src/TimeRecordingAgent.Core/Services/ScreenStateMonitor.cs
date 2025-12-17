using Microsoft.Win32;

namespace TimeRecordingAgent.Core.Services;

public sealed class ScreenStateMonitor : IDisposable
{
    private readonly ILogger<ScreenStateMonitor> _logger;
    private bool _isScreensaverRunning;
    private bool _isSuspended;
    private bool _isSessionLocked;
    private bool _disposed;

    public ScreenStateMonitor(ILogger<ScreenStateMonitor> logger)
    {
        _logger = logger;
        SystemEvents.PowerModeChanged += HandlePowerModeChanged;
        SystemEvents.SessionSwitch += HandleSessionSwitch;
        Refresh();
    }

    public bool IsScreenUnavailable => _isScreensaverRunning || _isSuspended || _isSessionLocked;

    public void Refresh()
    {
        if (NativeMethods.TryGetScreensaverRunning(out var running))
        {
            _isScreensaverRunning = running;
        }
    }

    private void HandlePowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _logger.LogDebug("System entering suspend");
                _isSuspended = true;
                break;
            case PowerModes.Resume:
                _logger.LogDebug("System resumed");
                _isSuspended = false;
                Refresh();
                break;
        }
    }

    private void HandleSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _logger.LogDebug("Session locked");
            _isSessionLocked = true;
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _logger.LogDebug("Session unlocked");
            _isSessionLocked = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
        SystemEvents.SessionSwitch -= HandleSessionSwitch;
        _disposed = true;
    }
}

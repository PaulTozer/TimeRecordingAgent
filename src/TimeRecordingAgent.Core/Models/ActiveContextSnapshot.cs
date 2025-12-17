namespace TimeRecordingAgent.Core.Models;

public sealed record ActiveContextSnapshot(
    IntPtr WindowHandle,
    string ProcessName,
    string WindowTitle,
    string DocumentName,
    DateTime StartedAtUtc);

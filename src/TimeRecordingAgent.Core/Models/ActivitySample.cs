namespace TimeRecordingAgent.Core.Models;

public sealed record ActivitySample(
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    string ProcessName,
    string WindowTitle,
    string DocumentName)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

namespace TimeRecordingAgent.Core.Models;

public sealed record ActivitySample(
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    string ProcessName,
    string WindowTitle,
    string DocumentName,
    string? ContentSnippet = null)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
}

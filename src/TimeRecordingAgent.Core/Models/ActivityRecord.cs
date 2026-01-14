namespace TimeRecordingAgent.Core.Models;

public sealed record ActivityRecord(
    long Id,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    string ProcessName,
    string WindowTitle,
    string DocumentName,
    bool IsApproved,
    string? GroupName,
    bool IsBillable = true,
    string? BillableCategory = null,
    string? Description = null)
{
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
    public DateTime StartedAtLocal => StartedAtUtc.ToLocalTime();
    public DateTime EndedAtLocal => EndedAtUtc.ToLocalTime();
}

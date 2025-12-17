using System;
using TimeRecordingAgent.Core.Models;

namespace TimeRecordingAgent.App.History;

public sealed class HistoryEntryViewModel
{
    public HistoryEntryViewModel(ActivityRecord record, bool isLive = false)
    {
        Record = record;
        IsLive = isLive;
    }

    public ActivityRecord Record { get; }
    public long Id => Record.Id;
    public string DocumentName => Record.DocumentName;
    public string ProcessName => Record.ProcessName;
    public string WindowTitle => Record.WindowTitle;
    public DateTime StartedLocal => Record.StartedAtLocal;
    public DateTime EndedLocal => Record.EndedAtLocal;
    public double DurationMinutes => Math.Round(Record.Duration.TotalMinutes, 2);
    public double DurationSeconds => Record.Duration.TotalSeconds;
    public TimeSpan Duration => Record.Duration;
    public string FormattedDuration => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}h {Duration.Minutes}m {Duration.Seconds}s"
        : Duration.TotalMinutes >= 1
            ? $"{(int)Duration.TotalMinutes}m {Duration.Seconds}s"
            : $"{Duration.Seconds}s";
    public bool IsApproved => Record.IsApproved;
    public bool IsBillable => Record.IsBillable;
    public bool IsLive { get; }
    public string Status => IsLive ? "Recording" : (Record.IsApproved ? "Approved" : "Pending");
    public string? GroupName => Record.GroupName;
    public string DisplayGroup => string.IsNullOrWhiteSpace(Record.GroupName) ? "(No Customer)" : Record.GroupName!;
    public string BillableDisplay => Record.IsBillable ? "Billable" : "Non-Billable";
    public string? BillableCategory => Record.BillableCategory;
    public string DisplayBillableCategory => string.IsNullOrWhiteSpace(Record.BillableCategory) ? "(No Category)" : Record.BillableCategory!;
}

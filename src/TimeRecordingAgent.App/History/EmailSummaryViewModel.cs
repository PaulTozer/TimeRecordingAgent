using System;

namespace TimeRecordingAgent.App.History;

public sealed class EmailSummaryViewModel
{
    public EmailSummaryViewModel(string subject, TimeSpan totalDuration, int viewCount)
    {
        Subject = subject;
        TotalDuration = totalDuration;
        ViewCount = viewCount;
    }

    public string Subject { get; }
    public TimeSpan TotalDuration { get; }
    public int ViewCount { get; }
    public double TotalMinutes => Math.Round(TotalDuration.TotalMinutes, 2);
    public string FormattedDuration => TotalDuration.TotalHours >= 1
        ? $"{(int)TotalDuration.TotalHours}h {TotalDuration.Minutes}m {TotalDuration.Seconds}s"
        : TotalDuration.TotalMinutes >= 1
            ? $"{(int)TotalDuration.TotalMinutes}m {TotalDuration.Seconds}s"
            : $"{TotalDuration.Seconds}s";
}

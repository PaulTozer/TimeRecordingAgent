using System;

namespace TimeRecordingAgent.App.History;

public sealed class GroupSummaryViewModel
{
    public GroupSummaryViewModel(string groupName, TimeSpan totalDuration)
    {
        GroupName = groupName;
        TotalDuration = totalDuration;
    }

    public string GroupName { get; }
    public TimeSpan TotalDuration { get; }
    public double TotalMinutes => Math.Round(TotalDuration.TotalMinutes, 2);
    public string FormattedDuration => TotalDuration.TotalHours >= 1
        ? $"{(int)TotalDuration.TotalHours}h {TotalDuration.Minutes}m {TotalDuration.Seconds}s"
        : TotalDuration.TotalMinutes >= 1
            ? $"{(int)TotalDuration.TotalMinutes}m {TotalDuration.Seconds}s"
            : $"{TotalDuration.Seconds}s";
}

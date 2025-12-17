namespace TimeRecordingAgent.Core.Models;

public sealed record DailySummaryRow(
    string ProcessName,
    string DocumentName,
    TimeSpan Duration)
{
    public double TotalMinutes => Math.Round(Duration.TotalMinutes, 2);
    public string FormattedDuration => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}h {Duration.Minutes}m {Duration.Seconds}s"
        : Duration.TotalMinutes >= 1
            ? $"{(int)Duration.TotalMinutes}m {Duration.Seconds}s"
            : $"{Duration.Seconds}s";
}

namespace TimeRecordingAgent.Core.Services;

public interface IOutlookContextReader
{
    string? TryGetActiveSubject();
    string? TryGetActiveSubject(string? windowTitle);
}

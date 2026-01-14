namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Represents context extracted from an Outlook item (email, meeting, etc.).
/// </summary>
public sealed record OutlookItemContext(
    string? Subject,
    string? BodySnippet,
    OutlookItemType ItemType);

/// <summary>
/// The type of Outlook item.
/// </summary>
public enum OutlookItemType
{
    Unknown,
    Mail,
    Meeting,
    Appointment,
    Task
}

public interface IOutlookContextReader
{
    string? TryGetActiveSubject();
    string? TryGetActiveSubject(string? windowTitle);
    
    /// <summary>
    /// Gets extended context from the active Outlook item, including a snippet of the body.
    /// </summary>
    OutlookItemContext? TryGetActiveItemContext(string? windowTitle);
}

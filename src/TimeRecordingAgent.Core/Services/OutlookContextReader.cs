using Microsoft.CSharp.RuntimeBinder;

namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Reads the active email subject from Outlook using late-bound COM automation.
/// This approach doesn't require Office PIAs to be installed - it works with any Outlook version.
/// </summary>
public sealed class OutlookContextReader : IOutlookContextReader
{
    private readonly ILogger<OutlookContextReader> _logger;
    private static readonly Guid OutlookApplicationClsid = new("0006F03A-0000-0000-C000-000000000046");

    public OutlookContextReader(ILogger<OutlookContextReader> logger)
    {
        _logger = logger;
    }

    public string? TryGetActiveSubject()
    {
        return TryGetActiveSubject(null);
    }

    public string? TryGetActiveSubject(string? windowTitle)
    {
        try
        {
            var application = GetOutlookApplication();
            if (application is null)
            {
                _logger.LogTrace("Outlook application not available via COM");
                return null;
            }

            // Check if window title suggests this is an inspector window (compose/read)
            // Inspector windows have titles like "Subject - Message", "Untitled - Message", 
            // "RE: Subject - Message", or just the subject when reading
            var isLikelyInspector = IsInspectorWindowTitle(windowTitle);
            
            if (isLikelyInspector)
            {
                // Prioritize inspector when we're likely in an inspector window
                string? subject = TryGetInspectorSubject(application);
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    _logger.LogDebug("Got email subject from inspector: {Subject}", subject);
                    return subject;
                }
                
                // For new/untitled emails, extract from window title
                if (windowTitle != null && IsNewEmailWindow(windowTitle))
                {
                    var extracted = ExtractSubjectFromWindowTitle(windowTitle);
                    _logger.LogDebug("Got email subject from compose window title: {Subject}", extracted);
                    return extracted;
                }
            }
            else
            {
                // Try inspector first anyway (might be reading an email)
                string? subject = TryGetInspectorSubject(application);
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    _logger.LogDebug("Got email subject from inspector: {Subject}", subject);
                    return subject;
                }

                // Fall back to selected item in explorer
                subject = TryGetExplorerSelectionSubject(application);
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    _logger.LogDebug("Got email subject from explorer selection: {Subject}", subject);
                    return subject;
                }
            }

            _logger.LogTrace("No active email subject found in Outlook");
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "COM error reading Outlook context");
        }
        catch (Exception ex) when (ex is InvalidCastException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Error accessing Outlook object model");
        }

        return null;
    }

    private static bool IsInspectorWindowTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return false;

        // Inspector windows typically end with " - Message", " - Meeting", " - Appointment", etc.
        // or are compose windows like "Untitled - Message"
        return windowTitle.Contains(" - Message", StringComparison.OrdinalIgnoreCase)
            || windowTitle.Contains(" - Meeting", StringComparison.OrdinalIgnoreCase)
            || windowTitle.Contains(" - Appointment", StringComparison.OrdinalIgnoreCase)
            || windowTitle.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)
            || windowTitle.StartsWith("RE:", StringComparison.OrdinalIgnoreCase)
            || windowTitle.StartsWith("FW:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNewEmailWindow(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return false;

        return windowTitle.Contains("Untitled", StringComparison.OrdinalIgnoreCase)
            || windowTitle.Contains(" - Message", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSubjectFromWindowTitle(string windowTitle)
    {
        // Window title format: "Subject - Message (HTML)" or "Untitled - Message (HTML)"
        // For compose windows, we use a stable identifier
        var messageIndex = windowTitle.IndexOf(" - Message", StringComparison.OrdinalIgnoreCase);
        if (messageIndex > 0)
        {
            var subject = windowTitle[..messageIndex].Trim();
            
            // Check if it's a new/untitled email
            if (subject.Equals("Untitled", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(subject))
            {
                return "(Composing New Email)";
            }
            
            // Check if it's a reply or forward
            if (subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ||
                subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase))
            {
                return $"(Composing) {subject}";
            }
            
            // For other compose windows with a subject, still use stable identifier
            // because the subject might change
            return "(Composing New Email)";
        }

        return windowTitle.Trim();
    }

    private string? TryGetInspectorSubject(dynamic application)
    {
        try
        {
            dynamic? inspector = application.ActiveInspector();
            if (inspector is null)
            {
                return null;
            }

            dynamic? currentItem = inspector.CurrentItem;
            if (currentItem is null)
            {
                return null;
            }

            // Check if it's a mail item (Class = 43 = olMail)
            int itemClass = currentItem.Class;
            if (itemClass == 43) // olMail
            {
                // Check if this is a compose window (unsent email)
                // Sent property is false for drafts/new emails being composed
                try
                {
                    bool sent = currentItem.Sent;
                    if (!sent)
                    {
                        // This is a compose window - use a stable identifier
                        // to aggregate time even if subject changes
                        string? subject = (string?)currentItem.Subject;
                        
                        // Check if it's a reply or forward (has RE:/FW: prefix or is based on another email)
                        bool isReplyOrForward = !string.IsNullOrEmpty(subject) && 
                            (subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ||
                             subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase) ||
                             subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ||
                             subject.StartsWith("Fw:", StringComparison.OrdinalIgnoreCase));
                        
                        if (isReplyOrForward)
                        {
                            // For replies/forwards, use the subject since it's stable
                            return $"(Composing) {subject}";
                        }
                        else
                        {
                            // For new emails, use a generic identifier so subject changes don't create new rows
                            return "(Composing New Email)";
                        }
                    }
                }
                catch
                {
                    // Sent property not accessible, fall through to return subject
                }
                
                return (string?)currentItem.Subject;
            }

            // Also handle meeting requests, appointments, etc.
            // Class 26 = olAppointment, Class 53 = olMeetingRequest
            if (itemClass is 26 or 53 or 54 or 55 or 56 or 57)
            {
                return (string?)currentItem.Subject;
            }
        }
        catch (COMException)
        {
            // Inspector might have been closed
        }
        catch (RuntimeBinderException)
        {
            // Property doesn't exist
        }

        return null;
    }

    private string? TryGetExplorerSelectionSubject(dynamic application)
    {
        try
        {
            dynamic? explorer = application.ActiveExplorer();
            if (explorer is null)
            {
                return null;
            }

            dynamic? selection = explorer.Selection;
            if (selection is null || (int)selection.Count == 0)
            {
                return null;
            }

            // Get first selected item (1-indexed in COM)
            dynamic? selectedItem = selection.Item(1);
            if (selectedItem is null)
            {
                return null;
            }

            int itemClass = selectedItem.Class;
            // Mail item or meeting-related items
            if (itemClass is 43 or 26 or 53 or 54 or 55 or 56 or 57)
            {
                return (string?)selectedItem.Subject;
            }
        }
        catch (COMException)
        {
            // Explorer or selection not accessible
        }
        catch (RuntimeBinderException)
        {
            // Property doesn't exist
        }

        return null;
    }

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr reserved, out IntPtr ppunk);

    private dynamic? GetOutlookApplication()
    {
        var clsid = OutlookApplicationClsid;
        var hr = GetActiveObject(ref clsid, IntPtr.Zero, out var punk);
        if (hr < 0 || punk == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.GetObjectForIUnknown(punk);
        }
        finally
        {
            Marshal.Release(punk);
        }
    }
}

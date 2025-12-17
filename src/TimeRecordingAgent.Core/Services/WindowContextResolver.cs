namespace TimeRecordingAgent.Core.Services;

public static class WindowContextResolver
{
    public static string ResolveDocumentName(string processName, string windowTitle, Func<string?, string?> outlookSubjectProvider)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return windowTitle.Trim();
        }

        var normalized = processName.ToLowerInvariant();
        if (normalized.Contains("winword"))
        {
            return ExtractBeforeSuffix(windowTitle, " - Word");
        }

        if (normalized.Contains("excel"))
        {
            return ExtractBeforeSuffix(windowTitle, " - Excel");
        }

        if (normalized.Contains("powerpnt"))
        {
            return ExtractBeforeSuffix(windowTitle, " - PowerPoint");
        }

        if (normalized.Contains("outlook"))
        {
            try
            {
                // Pass window title to help identify compose windows
                var subject = outlookSubjectProvider(windowTitle)?.Trim();
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    return subject;
                }
            }
            catch
            {
                // Outlook COM interop may fail if Office interop assemblies aren't available
                // Fall through to use window title
            }

            return ExtractBeforeSuffix(windowTitle, " - Outlook");
        }

        if (IsModernOutlookProcess(normalized))
        {
            return ExtractBeforeSuffix(windowTitle, " - Outlook");
        }

        return windowTitle.Trim();
    }

    private static bool IsModernOutlookProcess(string normalized)
    {
        return normalized is "olk" or "hxoutlook" or "hxmail";
    }

    private static string ExtractBeforeSuffix(string title, string suffix)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Unknown";
        }

        var index = title.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
        {
            return title.Trim();
        }

        return title[..index].Trim();
    }
}

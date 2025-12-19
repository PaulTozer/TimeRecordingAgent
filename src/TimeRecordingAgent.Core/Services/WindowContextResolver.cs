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
        
        // Teams handling - extract meeting/channel name, ignoring email suffixes
        if (normalized is "ms-teams" or "teams" or "msteams")
        {
            return ExtractTeamsDocumentName(windowTitle);
        }

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

    /// <summary>
    /// Extracts a stable document name from Teams window titles.
    /// Teams titles vary significantly: "Chat | Name | Microsoft | email | Microsoft Teams"
    /// or "Meeting Name | Microsoft | email | Microsoft Teams" 
    /// or just "Microsoft Teams"
    /// </summary>
    private static string ExtractTeamsDocumentName(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return "Microsoft Teams";
        }

        var title = windowTitle.Trim();

        // Generic Teams window
        if (title.Equals("Microsoft Teams", StringComparison.OrdinalIgnoreCase))
        {
            return "Microsoft Teams";
        }

        // Remove the " | Microsoft Teams" suffix if present
        const string teamsSuffix = " | Microsoft Teams";
        if (title.EndsWith(teamsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            title = title[..^teamsSuffix.Length];
        }

        // Teams titles often have format: "Context | Description | Microsoft | email@domain.com"
        // or for meetings: "Meeting Name | Microsoft | email@domain.com"
        // We want to extract just the first meaningful part
        
        var parts = title.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "Microsoft Teams";
        }

        // First part is usually the context type (Chat, Calendar, etc.) or meeting name
        var firstPart = parts[0];
        
        // If it's a meeting/call (title doesn't start with "Chat", "Calendar", "Activity", etc.)
        // and has a second part, the second part is usually the name
        if (parts.Length >= 2)
        {
            var secondPart = parts[1];
            
            // For "Chat | PersonName" or "Chat | GroupName" format
            if (firstPart.Equals("Chat", StringComparison.OrdinalIgnoreCase))
            {
                // Return "Teams Chat: PersonName" for clarity
                return $"Teams Chat: {secondPart}";
            }
            
            // For channels: "ChannelName (General)" or similar
            if (firstPart.Contains('(') || !IsMetadataPart(secondPart))
            {
                // First part looks like a channel/meeting name
                return $"Teams: {firstPart}";
            }
        }

        // If first part is not a navigation item, it's likely a meeting name
        if (!IsTeamsNavigationItem(firstPart))
        {
            return $"Teams: {firstPart}";
        }

        // Fallback to first part or generic
        return string.IsNullOrWhiteSpace(firstPart) ? "Microsoft Teams" : $"Teams: {firstPart}";
    }

    private static bool IsTeamsNavigationItem(string text)
    {
        return text.Equals("Chat", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Calendar", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Activity", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Teams", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Calls", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Files", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetadataPart(string text)
    {
        // Check if this looks like metadata (Microsoft, email address, etc.)
        return text.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            || text.Contains('@')
            || text.EndsWith(".com", StringComparison.OrdinalIgnoreCase);
    }
}

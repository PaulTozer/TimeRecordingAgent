using TimeRecordingAgent.Core.Services;

namespace TimeRecordingAgent.Core.Tests;

public class WindowContextResolverTests
{
    [Theory]
    [InlineData("winword", "Proposal.docx - Word", "Proposal.docx")]
    [InlineData("excel", "Budget.xlsx - Excel", "Budget.xlsx")]
    [InlineData("powerpnt", "Deck.pptx - PowerPoint", "Deck.pptx")]
    public void RemovesOfficeSuffixes(string process, string title, string expected)
    {
        var actual = WindowContextResolver.ResolveDocumentName(process, title, _ => null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OutlookUsesActiveSubjectWhenAvailable()
    {
        var actual = WindowContextResolver.ResolveDocumentName("outlook", "Inbox - Outlook", _ => "QBR Prep");
        Assert.Equal("QBR Prep", actual);
    }

    [Fact]
    public void OutlookFallsBackToTitle()
    {
        var actual = WindowContextResolver.ResolveDocumentName("outlook", "Inbox - Outlook", _ => null);
        Assert.Equal("Inbox", actual);
    }

    [Fact]
    public void GenericProcessReturnsTrimmedTitle()
    {
        var actual = WindowContextResolver.ResolveDocumentName("notepad", " notes.txt ", _ => null);
        Assert.Equal("notes.txt", actual);
    }
}

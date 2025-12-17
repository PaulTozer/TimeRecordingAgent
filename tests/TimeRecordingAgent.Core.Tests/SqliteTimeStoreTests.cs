using Microsoft.Extensions.Logging.Abstractions;
using TimeRecordingAgent.Core.Models;
using TimeRecordingAgent.Core.Storage;

namespace TimeRecordingAgent.Core.Tests;

public sealed class SqliteTimeStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteTimeStore _store;

    public SqliteTimeStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"time-agent-tests-{Guid.NewGuid():N}.db");
        _store = new SqliteTimeStore(_dbPath, NullLogger<SqliteTimeStore>.Instance);
    }

    [Fact]
    public void AggregatesDurationsByDocument()
    {
        var start = DateTime.UtcNow.AddMinutes(-10);
        _store.InsertSample(new ActivitySample(start, start.AddMinutes(5), "WINWORD.EXE", "Spec - Word", "Spec.docx"));
        _store.InsertSample(new ActivitySample(start.AddMinutes(6), start.AddMinutes(8), "WINWORD.EXE", "Spec - Word", "Spec.docx"));

        var summary = _store.GetDailySummary(DateOnly.FromDateTime(DateTime.Now));
        Assert.Single(summary);
        Assert.Equal("Spec.docx", summary[0].DocumentName);
        Assert.InRange(summary[0].TotalMinutes, 6.9, 7.1);
    }

    [Fact]
    public void CanApproveGroupAndDeleteSamples()
    {
        var baseTime = DateTime.UtcNow.AddMinutes(-30);
        _store.InsertSample(new ActivitySample(baseTime, baseTime.AddMinutes(3), "WINWORD.EXE", "Design Spec", "Design.docx"));
        _store.InsertSample(new ActivitySample(baseTime.AddMinutes(4), baseTime.AddMinutes(6), "EXCEL.EXE", "Budget", "Budget.xlsx"));

        var recent = _store.GetRecentSamples(10);
        Assert.Equal(2, recent.Count);

        var toApprove = recent[0].Id;
        var toDelete = recent[1].Id;

        _store.SetApproval(new[] { toApprove }, true);
        _store.SetGroupName(new[] { toApprove }, "Client A");
        _store.DeleteSamples(new[] { toDelete });

        var updated = _store.GetRecentSamples(10);
        Assert.Single(updated);
        Assert.True(updated[0].IsApproved);
        Assert.Equal("Client A", updated[0].GroupName);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}

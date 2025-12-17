using TimeRecordingAgent.Core.Models;
using TimeRecordingAgent.Core.Storage;

namespace TimeRecordingAgent.Core.Services;

public sealed class RecordingCoordinator : IDisposable
{
    private readonly ForegroundWindowPoller _poller;
    private readonly SqliteTimeStore _store;
    private readonly ILogger<RecordingCoordinator> _logger;
    private bool _isRunning;

    public RecordingCoordinator(
        ForegroundWindowPoller poller,
        SqliteTimeStore store,
        ILogger<RecordingCoordinator> logger)
    {
        _poller = poller;
        _store = store;
        _logger = logger;
        _poller.SampleFinalized += HandleSampleFinalized;
        _poller.ContextChanged += HandleContextChanged;
    }

    public event EventHandler<ActivitySample>? SampleStored;
    public event EventHandler<ActiveContextSnapshot?>? ContextChanged;

    public bool IsRunning => _isRunning;
    public ActiveContextSnapshot? CurrentContext { get; private set; }

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _poller.Start();
        _isRunning = true;
        _logger.LogInformation("Recording started.");
    }

    public void Pause()
    {
        if (!_isRunning)
        {
            return;
        }

        _poller.Stop();
        _isRunning = false;
        _logger.LogInformation("Recording paused.");
    }

    public IReadOnlyList<DailySummaryRow> GetDailySummary(DateOnly date)
    {
        return _store.GetDailySummary(date);
    }

    public IReadOnlyList<ActivityRecord> GetRecentSamples(int take = 250)
    {
        return _store.GetRecentSamples(take);
    }

    public ActivityRecord? GetCurrentActivity()
    {
        var snapshot = CurrentContext;
        if (snapshot is null)
        {
            return null;
        }

        return new ActivityRecord(
            -1,
            snapshot.StartedAtUtc,
            DateTime.UtcNow,
            snapshot.ProcessName,
            snapshot.WindowTitle,
            snapshot.DocumentName,
            false,
            null);
    }

    public void FlushCurrentSample()
    {
        _logger.LogDebug("Coordinator requested poller flush to capture current activity context.");
        _poller.FlushActive();
    }

    public void SetApproval(IEnumerable<long> ids, bool isApproved)
    {
        _store.SetApproval(ids, isApproved);
    }

    public void SetGroupName(IEnumerable<long> ids, string? groupName)
    {
        _store.SetGroupName(ids, groupName);
    }

    public void SetBillable(IEnumerable<long> ids, bool isBillable)
    {
        _store.SetBillable(ids, isBillable);
    }

    public void SetBillableCategory(IEnumerable<long> ids, string? category)
    {
        _store.SetBillableCategory(ids, category);
    }

    public void DeleteSamples(IEnumerable<long> ids)
    {
        _store.DeleteSamples(ids);
    }

    public void Dispose()
    {
        _poller.SampleFinalized -= HandleSampleFinalized;
        _poller.ContextChanged -= HandleContextChanged;
        _poller.Dispose();
        _store.Dispose();
    }

    private void HandleSampleFinalized(object? sender, ActivitySample sample)
    {
        _logger.LogDebug(
            "Sample finalized: {Process} | '{Title}' | '{Document}' spanning {DurationSeconds:F1}s (start {Start:o}).",
            sample.ProcessName,
            sample.WindowTitle,
            sample.DocumentName ?? "<none>",
            sample.Duration.TotalSeconds,
            sample.StartedAtUtc);
        _store.InsertSample(sample);
        _logger.LogTrace("Sample stored with end {End:o}.", sample.EndedAtUtc);
        SampleStored?.Invoke(this, sample);
    }

    private void HandleContextChanged(object? sender, ActiveContextSnapshot? snapshot)
    {
        CurrentContext = snapshot;
        if (snapshot is null)
        {
            _logger.LogTrace("Context cleared (no active foreground window).");
        }
        else
        {
            _logger.LogTrace(
                "Context changed to {Process} | '{Title}' | '{Document}' (started {Start:o}).",
                snapshot.ProcessName,
                snapshot.WindowTitle,
                snapshot.DocumentName ?? "<none>",
                snapshot.StartedAtUtc);
        }
        ContextChanged?.Invoke(this, snapshot);
    }
}

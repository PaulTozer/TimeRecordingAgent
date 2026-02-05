using System.Data;
using Npgsql;
using TimeRecordingAgent.Core.Configuration;
using TimeRecordingAgent.Core.Models;

namespace TimeRecordingAgent.Core.Services;

/// <summary>
/// Service that syncs local timesheet entries to an Azure cloud database.
/// Supports Azure Database for PostgreSQL.
/// </summary>
public sealed class CloudSyncService : IDisposable
{
    private readonly ILogger<CloudSyncService> _logger;
    private readonly object _lock = new();
    private string? _connectionString;
    private string? _userId;
    private bool _isConfigured;
    private bool _isEnabled;
    private bool _syncApprovedOnly;
    private CloudDatabaseProvider _provider;
    private System.Threading.Timer? _syncTimer;
    private Func<IReadOnlyList<ActivityRecord>>? _getEntriesFunc;

    public CloudSyncService(ILogger<CloudSyncService> logger)
    {
        _logger = logger;
    }

    public CloudSyncService(ILogger<CloudSyncService> logger, CloudSyncSettings settings)
        : this(logger)
    {
        Configure(settings);
    }

    /// <summary>
    /// Gets whether the cloud sync service is available and enabled.
    /// </summary>
    public bool IsEnabled => _isEnabled && _isConfigured;

    /// <summary>
    /// Gets whether the service has been configured with valid settings.
    /// </summary>
    public bool IsConfigured => _isConfigured;

    /// <summary>
    /// Configures the cloud sync service with new settings.
    /// </summary>
    public void Configure(CloudSyncSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.Enabled;
            _syncApprovedOnly = settings.SyncApprovedOnly;
            _provider = settings.Provider;

            if (!settings.IsConfigured)
            {
                _logger.LogInformation("Cloud Sync is not configured: missing connection string or user ID.");
                _isConfigured = false;
                return;
            }

            _connectionString = settings.ConnectionString;
            _userId = settings.UserId;
            _isConfigured = true;

            _logger.LogInformation(
                "Cloud Sync service configured for {Provider}, user: {UserId}, enabled: {Enabled}, sync interval: {Interval} minutes",
                _provider,
                _userId,
                _isEnabled,
                settings.SyncIntervalMinutes);
        }
    }

    /// <summary>
    /// Starts automatic background syncing.
    /// </summary>
    /// <param name="getEntriesFunc">Function to retrieve entries from local storage.</param>
    /// <param name="intervalMinutes">Sync interval in minutes.</param>
    public void StartAutoSync(Func<IReadOnlyList<ActivityRecord>> getEntriesFunc, int intervalMinutes = 15)
    {
        _getEntriesFunc = getEntriesFunc;
        _syncTimer?.Dispose();
        _syncTimer = new System.Threading.Timer(
            async _ => await SyncPendingEntriesAsync(),
            null,
            TimeSpan.FromMinutes(1), // Initial delay
            TimeSpan.FromMinutes(intervalMinutes));

        _logger.LogInformation("Auto-sync started with {Interval} minute interval.", intervalMinutes);
    }

    /// <summary>
    /// Stops automatic background syncing.
    /// </summary>
    public void StopAutoSync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        _logger.LogInformation("Auto-sync stopped.");
    }

    /// <summary>
    /// Syncs pending entries from local storage to the cloud.
    /// </summary>
    private async Task SyncPendingEntriesAsync()
    {
        if (!IsEnabled || _getEntriesFunc is null)
            return;

        try
        {
            var entries = _getEntriesFunc();
            var toSync = _syncApprovedOnly
                ? entries.Where(e => e.IsApproved).ToList()
                : entries.ToList();

            if (toSync.Count > 0)
            {
                await SyncEntriesAsync(toSync);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-sync failed.");
        }
    }

    /// <summary>
    /// Syncs a collection of activity records to the cloud database.
    /// </summary>
    public async Task<int> SyncEntriesAsync(IReadOnlyList<ActivityRecord> entries, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("Cloud Sync is not enabled or configured.");
            return 0;
        }

        if (entries.Count == 0)
        {
            return 0;
        }

        return _provider switch
        {
            CloudDatabaseProvider.PostgreSQL => await SyncToPostgreSQLAsync(entries, cancellationToken),
            CloudDatabaseProvider.AzureSQL => await SyncToAzureSQLAsync(entries, cancellationToken),
            _ => throw new NotSupportedException($"Provider {_provider} is not supported.")
        };
    }

    /// <summary>
    /// Ensures the required table exists in the cloud database.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_connectionString))
            return;

        if (_provider == CloudDatabaseProvider.PostgreSQL)
        {
            await EnsurePostgreSQLSchemaAsync(cancellationToken);
        }
        else if (_provider == CloudDatabaseProvider.AzureSQL)
        {
            await EnsureAzureSQLSchemaAsync(cancellationToken);
        }
    }

    private async Task EnsurePostgreSQLSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS timesheet_entries (
                id BIGSERIAL PRIMARY KEY,
                local_id BIGINT NOT NULL,
                user_id VARCHAR(255) NOT NULL,
                started_at TIMESTAMPTZ NOT NULL,
                ended_at TIMESTAMPTZ NOT NULL,
                duration_hours DECIMAL(10,2) NOT NULL,
                process_name VARCHAR(255) NOT NULL,
                document_name VARCHAR(500) NOT NULL,
                window_title VARCHAR(1000),
                group_name VARCHAR(255),
                is_billable BOOLEAN NOT NULL DEFAULT TRUE,
                billable_category VARCHAR(100),
                description TEXT,
                is_approved BOOLEAN NOT NULL DEFAULT FALSE,
                verification_status VARCHAR(50),
                verification_notes TEXT,
                synced_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(user_id, local_id)
            );

            CREATE INDEX IF NOT EXISTS idx_timesheet_user_date ON timesheet_entries(user_id, started_at);
            CREATE INDEX IF NOT EXISTS idx_timesheet_verification ON timesheet_entries(user_id, verification_status);
        ";
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("PostgreSQL schema ensured.");
    }

    private async Task EnsureAzureSQLSchemaAsync(CancellationToken cancellationToken)
    {
        // Azure SQL implementation would go here
        // Using Microsoft.Data.SqlClient
        _logger.LogWarning("Azure SQL schema creation not yet implemented.");
        await Task.CompletedTask;
    }

    private async Task<int> SyncToPostgreSQLAsync(IReadOnlyList<ActivityRecord> entries, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var syncedCount = 0;

        foreach (var entry in entries)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO timesheet_entries 
                        (local_id, user_id, started_at, ended_at, duration_hours, 
                         process_name, document_name, window_title, group_name,
                         is_billable, billable_category, description, is_approved, synced_at)
                    VALUES 
                        (@localId, @userId, @startedAt, @endedAt, @durationHours,
                         @processName, @documentName, @windowTitle, @groupName,
                         @isBillable, @billableCategory, @description, @isApproved, @syncedAt)
                    ON CONFLICT (user_id, local_id) 
                    DO UPDATE SET
                        ended_at = EXCLUDED.ended_at,
                        duration_hours = EXCLUDED.duration_hours,
                        group_name = EXCLUDED.group_name,
                        is_billable = EXCLUDED.is_billable,
                        billable_category = EXCLUDED.billable_category,
                        description = EXCLUDED.description,
                        is_approved = EXCLUDED.is_approved,
                        synced_at = EXCLUDED.synced_at;
                ";

                command.Parameters.AddWithValue("localId", entry.Id);
                command.Parameters.AddWithValue("userId", _userId!);
                command.Parameters.AddWithValue("startedAt", entry.StartedAtUtc);
                command.Parameters.AddWithValue("endedAt", entry.EndedAtUtc);
                command.Parameters.AddWithValue("durationHours", Math.Round(entry.Duration.TotalHours, 2));
                command.Parameters.AddWithValue("processName", entry.ProcessName);
                command.Parameters.AddWithValue("documentName", entry.DocumentName);
                command.Parameters.AddWithValue("windowTitle", entry.WindowTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("groupName", entry.GroupName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("isBillable", entry.IsBillable);
                command.Parameters.AddWithValue("billableCategory", entry.BillableCategory ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("description", entry.Description ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("isApproved", entry.IsApproved);
                command.Parameters.AddWithValue("syncedAt", DateTime.UtcNow);

                await command.ExecuteNonQueryAsync(cancellationToken);
                syncedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync entry {EntryId}.", entry.Id);
            }
        }

        _logger.LogInformation("Synced {Count} entries to PostgreSQL for user {UserId}.", syncedCount, _userId);
        return syncedCount;
    }

    private async Task<int> SyncToAzureSQLAsync(IReadOnlyList<ActivityRecord> entries, CancellationToken cancellationToken)
    {
        // Azure SQL implementation would go here
        _logger.LogWarning("Azure SQL sync not yet implemented.");
        return await Task.FromResult(0);
    }

    /// <summary>
    /// Gets entries for a specific user and date range (for the Azure Function to call).
    /// </summary>
    public async Task<IReadOnlyList<CloudTimesheetEntry>> GetEntriesAsync(
        string userId,
        DateOnly date,
        string? verificationStatus = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_connectionString))
            return [];

        if (_provider == CloudDatabaseProvider.PostgreSQL)
        {
            return await GetEntriesFromPostgreSQLAsync(userId, date, verificationStatus, cancellationToken);
        }

        return [];
    }

    private async Task<IReadOnlyList<CloudTimesheetEntry>> GetEntriesFromPostgreSQLAsync(
        string userId,
        DateOnly date,
        string? verificationStatus,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var dateStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dateEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var sql = @"
            SELECT id, local_id, user_id, started_at, ended_at, duration_hours,
                   process_name, document_name, window_title, group_name,
                   is_billable, billable_category, description, is_approved,
                   verification_status, verification_notes, synced_at
            FROM timesheet_entries
            WHERE user_id = @userId
              AND started_at >= @dateStart
              AND started_at < @dateEnd";

        if (!string.IsNullOrEmpty(verificationStatus))
        {
            sql += " AND (verification_status = @verificationStatus OR verification_status IS NULL)";
        }

        sql += " ORDER BY started_at";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("dateStart", dateStart);
        command.Parameters.AddWithValue("dateEnd", dateEnd);
        if (!string.IsNullOrEmpty(verificationStatus))
        {
            command.Parameters.AddWithValue("verificationStatus", verificationStatus);
        }

        var results = new List<CloudTimesheetEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CloudTimesheetEntry(
                Id: reader.GetInt64(0),
                LocalId: reader.GetInt64(1),
                UserId: reader.GetString(2),
                StartedAt: reader.GetDateTime(3),
                EndedAt: reader.GetDateTime(4),
                DurationHours: reader.GetDecimal(5),
                ProcessName: reader.GetString(6),
                DocumentName: reader.GetString(7),
                WindowTitle: reader.IsDBNull(8) ? null : reader.GetString(8),
                GroupName: reader.IsDBNull(9) ? null : reader.GetString(9),
                IsBillable: reader.GetBoolean(10),
                BillableCategory: reader.IsDBNull(11) ? null : reader.GetString(11),
                Description: reader.IsDBNull(12) ? null : reader.GetString(12),
                IsApproved: reader.GetBoolean(13),
                VerificationStatus: reader.IsDBNull(14) ? null : reader.GetString(14),
                VerificationNotes: reader.IsDBNull(15) ? null : reader.GetString(15),
                SyncedAt: reader.GetDateTime(16)
            ));
        }

        return results;
    }

    /// <summary>
    /// Updates the verification status for an entry.
    /// </summary>
    public async Task UpdateVerificationStatusAsync(
        long entryId,
        string status,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_connectionString))
            return;

        if (_provider == CloudDatabaseProvider.PostgreSQL)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE timesheet_entries
                SET verification_status = @status,
                    verification_notes = @notes
                WHERE id = @entryId;
            ";
            command.Parameters.AddWithValue("entryId", entryId);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("notes", notes ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
    }
}

/// <summary>
/// Represents a timesheet entry stored in the cloud database.
/// </summary>
public sealed record CloudTimesheetEntry(
    long Id,
    long LocalId,
    string UserId,
    DateTime StartedAt,
    DateTime EndedAt,
    decimal DurationHours,
    string ProcessName,
    string DocumentName,
    string? WindowTitle,
    string? GroupName,
    bool IsBillable,
    string? BillableCategory,
    string? Description,
    bool IsApproved,
    string? VerificationStatus,
    string? VerificationNotes,
    DateTime SyncedAt);

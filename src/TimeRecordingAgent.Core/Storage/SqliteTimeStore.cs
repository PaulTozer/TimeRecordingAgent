using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using TimeRecordingAgent.Core.Models;

namespace TimeRecordingAgent.Core.Storage;

public sealed class SqliteTimeStore : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteTimeStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteTimeStore(string databasePath, ILogger<SqliteTimeStore> logger)
    {
        _logger = logger;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();

        EnsureSchema();
    }

    public string ConnectionString => _connectionString;

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS activity_log (
                id INTEGER PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                process_name TEXT NOT NULL,
                document_name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                is_approved INTEGER NOT NULL DEFAULT 0,
                group_name TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_activity_log_started_at ON activity_log(started_at);
        ";
        command.ExecuteNonQuery();

        EnsureColumn(connection, "activity_log", "is_approved", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "activity_log", "group_name", "TEXT NULL");
        EnsureColumn(connection, "activity_log", "is_billable", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "activity_log", "billable_category", "TEXT NULL");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            var existing = reader.GetString(1);
            if (string.Equals(existing, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    public void InsertSample(ActivitySample sample)
    {
        _gate.Wait();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            // Look for an existing row with the same process + document from today
            // to aggregate time instead of creating a new row
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            
            using var findCmd = connection.CreateCommand();
            findCmd.CommandText = @"
                SELECT id, started_at, ended_at 
                FROM activity_log 
                WHERE process_name = $process 
                  AND document_name = $document 
                  AND started_at >= $todayStart 
                  AND started_at < $todayEnd
                ORDER BY id DESC
                LIMIT 1;
            ";
            findCmd.Parameters.AddWithValue("$process", sample.ProcessName);
            findCmd.Parameters.AddWithValue("$document", sample.DocumentName);
            findCmd.Parameters.AddWithValue("$todayStart", todayStart.ToString("o"));
            findCmd.Parameters.AddWithValue("$todayEnd", todayEnd.ToString("o"));
            
            using var reader = findCmd.ExecuteReader();
            if (reader.Read())
            {
                // Found existing row - update it by adding the new duration
                var existingId = reader.GetInt64(0);
                var existingStart = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind);
                var existingEnd = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind);
                reader.Close();
                
                // Calculate new total duration: existing duration + new sample duration
                var existingDuration = existingEnd - existingStart;
                var additionalDuration = sample.Duration;
                var newTotalDuration = existingDuration + additionalDuration;
                
                // Update ended_at to reflect the new total duration (keeping original start)
                var newEnd = existingStart + newTotalDuration;
                
                using var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE activity_log 
                    SET ended_at = $ended, window_title = $title
                    WHERE id = $id;
                ";
                updateCmd.Parameters.AddWithValue("$id", existingId);
                updateCmd.Parameters.AddWithValue("$ended", newEnd.ToString("o"));
                updateCmd.Parameters.AddWithValue("$title", sample.WindowTitle); // Update to latest window title
                updateCmd.ExecuteNonQuery();
                
                _logger.LogDebug(
                    "Aggregated {Duration:F1}s to existing row {Id} for '{Document}' (total now {Total:F1}s).",
                    additionalDuration.TotalSeconds,
                    existingId,
                    sample.DocumentName,
                    newTotalDuration.TotalSeconds);
            }
            else
            {
                reader.Close();
                
                // No existing row for today - insert new
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO activity_log (started_at, ended_at, process_name, document_name, window_title)
                    VALUES ($started, $ended, $process, $document, $title);
                ";
                insertCmd.Parameters.AddWithValue("$started", sample.StartedAtUtc.ToString("o"));
                insertCmd.Parameters.AddWithValue("$ended", sample.EndedAtUtc.ToString("o"));
                insertCmd.Parameters.AddWithValue("$process", sample.ProcessName);
                insertCmd.Parameters.AddWithValue("$document", sample.DocumentName);
                insertCmd.Parameters.AddWithValue("$title", sample.WindowTitle);
                insertCmd.ExecuteNonQuery();
                
                _logger.LogDebug(
                    "Inserted new row for '{Document}' with {Duration:F1}s.",
                    sample.DocumentName,
                    sample.Duration.TotalSeconds);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<DailySummaryRow> GetDailySummary(DateOnly date)
    {
        var startLocal = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local);
        var start = startLocal.ToUniversalTime();
        var end = start.AddDays(1);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT process_name, document_name,
                   SUM((julianday(ended_at) - julianday(started_at)) * 86400.0) AS total_seconds
            FROM activity_log
            WHERE started_at >= $start AND started_at < $end
            GROUP BY process_name, document_name
            ORDER BY total_seconds DESC;
        ";
        command.Parameters.AddWithValue("$start", start.ToString("o"));
        command.Parameters.AddWithValue("$end", end.ToString("o"));

        var results = new List<DailySummaryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var process = reader.GetString(0);
            var document = reader.GetString(1);
            var seconds = reader.GetDouble(2);
            results.Add(new DailySummaryRow(process, document, TimeSpan.FromSeconds(seconds)));
        }

        return results;
    }

    public IReadOnlyList<ActivityRecord> GetRecentSamples(int take = 250)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, started_at, ended_at, process_name, window_title, document_name, is_approved, group_name, is_billable, billable_category
            FROM activity_log
            ORDER BY is_approved ASC, started_at DESC
            LIMIT $take;
        ";
        command.Parameters.AddWithValue("$take", take);

        var results = new List<ActivityRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var started = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var ended = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var process = reader.GetString(3);
            var window = reader.GetString(4);
            var document = reader.GetString(5);
            var approved = reader.GetInt32(6) != 0;
            var group = reader.IsDBNull(7) ? null : reader.GetString(7);
            var billable = reader.IsDBNull(8) || reader.GetInt32(8) != 0;
            var billableCategory = reader.IsDBNull(9) ? null : reader.GetString(9);
            results.Add(new ActivityRecord(id, started, ended, process, window, document, approved, group, billable, billableCategory));
        }

        return results;
    }

    public void SetApproval(IEnumerable<long> ids, bool isApproved)
    {
        ExecuteIdUpdate(ids, (command, placeholder) =>
        {
            command.CommandText = $"UPDATE activity_log SET is_approved = $approved WHERE id IN ({placeholder});";
            command.Parameters.AddWithValue("$approved", isApproved ? 1 : 0);
        });
    }

    public void SetGroupName(IEnumerable<long> ids, string? groupName)
    {
        ExecuteIdUpdate(ids, (command, placeholder) =>
        {
            command.CommandText = $"UPDATE activity_log SET group_name = $group WHERE id IN ({placeholder});";
            command.Parameters.AddWithValue("$group", string.IsNullOrWhiteSpace(groupName) ? DBNull.Value : groupName);
        });
    }

    public void SetBillable(IEnumerable<long> ids, bool isBillable)
    {
        ExecuteIdUpdate(ids, (command, placeholder) =>
        {
            command.CommandText = $"UPDATE activity_log SET is_billable = $billable WHERE id IN ({placeholder});";
            command.Parameters.AddWithValue("$billable", isBillable ? 1 : 0);
        });
    }

    public void SetBillableCategory(IEnumerable<long> ids, string? category)
    {
        ExecuteIdUpdate(ids, (command, placeholder) =>
        {
            command.CommandText = $"UPDATE activity_log SET billable_category = $category WHERE id IN ({placeholder});";
            command.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(category) ? DBNull.Value : category);
        });
    }

    public void DeleteSamples(IEnumerable<long> ids)
    {
        ExecuteIdUpdate(ids, (command, placeholder) =>
        {
            command.CommandText = $"DELETE FROM activity_log WHERE id IN ({placeholder});";
        });
    }

    private void ExecuteIdUpdate(IEnumerable<long> ids, Action<SqliteCommand, string> configureCommand)
    {
        var idArray = ids as long[] ?? ids.ToArray();
        if (idArray.Length == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        var placeholder = BuildInClause(command, idArray);
        configureCommand(command, placeholder);
        command.ExecuteNonQuery();
    }

    private static string BuildInClause(SqliteCommand command, IReadOnlyList<long> ids)
    {
        var parameters = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"$id{i}";
            command.Parameters.AddWithValue(name, ids[i]);
            parameters[i] = name;
        }

        return string.Join(",", parameters);
    }

    public void Dispose()
    {
        _gate.Dispose();
        SqliteConnection.ClearAllPools();
    }
}

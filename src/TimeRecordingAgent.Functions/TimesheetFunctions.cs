using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace TimeRecordingAgent.Functions;

/// <summary>
/// Azure Functions that expose timesheet data via OpenAPI for the Foundry Agent.
/// These endpoints allow the agent to query timesheet entries and update verification status.
/// </summary>
public class TimesheetFunctions
{
    private readonly ILogger<TimesheetFunctions> _logger;
    private readonly string? _connectionString;
    private readonly string? _apiKey;

    public TimesheetFunctions(ILogger<TimesheetFunctions> logger)
    {
        _logger = logger;
        _connectionString = Environment.GetEnvironmentVariable("PostgreSQL__ConnectionString");
        _apiKey = Environment.GetEnvironmentVariable("API_KEY");
    }

    /// <summary>
    /// Gets timesheet entries for a specific user and date.
    /// This is the primary endpoint for the Foundry agent to retrieve entries for verification.
    /// </summary>
    /// <remarks>
    /// OpenAPI Operation: GetTimesheetEntries
    /// Path: /api/timesheets/{userId}/{date}
    /// Method: GET
    /// Auth: API Key (x-api-key header)
    /// </remarks>
    [Function("GetTimesheetEntries")]
    public async Task<HttpResponseData> GetTimesheetEntries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesheets/{userId}/{date}")] HttpRequestData req,
        string userId,
        string date)
    {
        _logger.LogInformation("Getting timesheet entries for user {UserId} on {Date}", userId, date);

        // Validate API key
        if (!ValidateApiKey(req))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Invalid or missing API key");
            return unauthorized;
        }

        if (!DateOnly.TryParse(date, out var parsedDate))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid date format. Use yyyy-MM-dd");
            return badRequest;
        }

        try
        {
            var entries = await GetEntriesFromDatabaseAsync(userId, parsedDate);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting timesheet entries");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error: {ex.Message}");
            return error;
        }
    }

    /// <summary>
    /// Gets all unverified timesheet entries for a user.
    /// Useful for the agent to find entries that need compliance checking.
    /// </summary>
    [Function("GetUnverifiedEntries")]
    public async Task<HttpResponseData> GetUnverifiedEntries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesheets/{userId}/unverified")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Getting unverified entries for user {UserId}", userId);

        if (!ValidateApiKey(req))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Invalid or missing API key");
            return unauthorized;
        }

        try
        {
            var entries = await GetUnverifiedEntriesFromDatabaseAsync(userId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(entries, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unverified entries");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error: {ex.Message}");
            return error;
        }
    }

    /// <summary>
    /// Updates the verification status of a timesheet entry.
    /// Called by the Foundry agent after reviewing an entry.
    /// </summary>
    [Function("UpdateVerificationStatus")]
    public async Task<HttpResponseData> UpdateVerificationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "timesheets/verify/{entryId}")] HttpRequestData req,
        long entryId)
    {
        _logger.LogInformation("Updating verification status for entry {EntryId}", entryId);

        if (!ValidateApiKey(req))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Invalid or missing API key");
            return unauthorized;
        }

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var update = JsonSerializer.Deserialize<VerificationUpdate>(requestBody ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (update is null || string.IsNullOrEmpty(update.Status))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync("Request body must include 'status' field");
                return badRequest;
            }

            await UpdateVerificationInDatabaseAsync(entryId, update.Status, update.Notes);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Entry {entryId} verification updated to '{update.Status}'");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating verification status");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error: {ex.Message}");
            return error;
        }
    }

    /// <summary>
    /// Gets a summary of entries for a date range.
    /// Useful for generating compliance reports.
    /// </summary>
    [Function("GetTimesheetSummary")]
    public async Task<HttpResponseData> GetTimesheetSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "timesheets/{userId}/summary")] HttpRequestData req,
        string userId)
    {
        _logger.LogInformation("Getting timesheet summary for user {UserId}", userId);

        if (!ValidateApiKey(req))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteStringAsync("Invalid or missing API key");
            return unauthorized;
        }

        // Parse date range from query parameters
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var startDateStr = query["startDate"];
        var endDateStr = query["endDate"];

        if (!DateOnly.TryParse(startDateStr, out var startDate))
            startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));

        if (!DateOnly.TryParse(endDateStr, out var endDate))
            endDate = DateOnly.FromDateTime(DateTime.Today);

        try
        {
            var summary = await GetSummaryFromDatabaseAsync(userId, startDate, endDate);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting timesheet summary");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync($"Error: {ex.Message}");
            return error;
        }
    }

    private bool ValidateApiKey(HttpRequestData req)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return true; // No API key configured, allow all requests (for development)

        if (!req.Headers.TryGetValues("x-api-key", out var values))
            return false;

        return values.FirstOrDefault() == _apiKey;
    }

    private async Task<List<TimesheetEntry>> GetEntriesFromDatabaseAsync(string userId, DateOnly date)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("PostgreSQL connection string not configured");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var dateStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dateEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using var command = new NpgsqlCommand(@"
            SELECT id, local_id, user_id, started_at, ended_at, duration_hours,
                   process_name, document_name, window_title, group_name,
                   is_billable, billable_category, description, is_approved,
                   verification_status, verification_notes, synced_at
            FROM timesheet_entries
            WHERE user_id = @userId
              AND started_at >= @dateStart
              AND started_at < @dateEnd
            ORDER BY started_at", connection);

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("dateStart", dateStart);
        command.Parameters.AddWithValue("dateEnd", dateEnd);

        return await ReadEntriesAsync(command);
    }

    private async Task<List<TimesheetEntry>> GetUnverifiedEntriesFromDatabaseAsync(string userId)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("PostgreSQL connection string not configured");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
            SELECT id, local_id, user_id, started_at, ended_at, duration_hours,
                   process_name, document_name, window_title, group_name,
                   is_billable, billable_category, description, is_approved,
                   verification_status, verification_notes, synced_at
            FROM timesheet_entries
            WHERE user_id = @userId
              AND (verification_status IS NULL OR verification_status = 'Pending')
            ORDER BY started_at DESC
            LIMIT 100", connection);

        command.Parameters.AddWithValue("userId", userId);

        return await ReadEntriesAsync(command);
    }

    private async Task<List<TimesheetEntry>> ReadEntriesAsync(NpgsqlCommand command)
    {
        var results = new List<TimesheetEntry>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new TimesheetEntry(
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

    private async Task UpdateVerificationInDatabaseAsync(long entryId, string status, string? notes)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("PostgreSQL connection string not configured");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(@"
            UPDATE timesheet_entries
            SET verification_status = @status,
                verification_notes = @notes
            WHERE id = @entryId", connection);

        command.Parameters.AddWithValue("entryId", entryId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("notes", notes ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<TimesheetSummary> GetSummaryFromDatabaseAsync(string userId, DateOnly startDate, DateOnly endDate)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("PostgreSQL connection string not configured");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var dateStart = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dateEnd = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using var command = new NpgsqlCommand(@"
            SELECT 
                COUNT(*) as total_entries,
                SUM(duration_hours) as total_hours,
                SUM(CASE WHEN is_billable THEN duration_hours ELSE 0 END) as billable_hours,
                SUM(CASE WHEN verification_status = 'Compliant' THEN 1 ELSE 0 END) as compliant_count,
                SUM(CASE WHEN verification_status = 'NonCompliant' THEN 1 ELSE 0 END) as non_compliant_count,
                SUM(CASE WHEN verification_status IS NULL OR verification_status = 'Pending' THEN 1 ELSE 0 END) as pending_count
            FROM timesheet_entries
            WHERE user_id = @userId
              AND started_at >= @dateStart
              AND started_at < @dateEnd", connection);

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("dateStart", dateStart);
        command.Parameters.AddWithValue("dateEnd", dateEnd);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new TimesheetSummary(
                UserId: userId,
                StartDate: startDate.ToString("yyyy-MM-dd"),
                EndDate: endDate.ToString("yyyy-MM-dd"),
                TotalEntries: reader.GetInt64(0),
                TotalHours: reader.IsDBNull(1) ? 0 : reader.GetDecimal(1),
                BillableHours: reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                CompliantCount: reader.GetInt64(3),
                NonCompliantCount: reader.GetInt64(4),
                PendingCount: reader.GetInt64(5)
            );
        }

        return new TimesheetSummary(userId, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), 0, 0, 0, 0, 0, 0);
    }
}

/// <summary>
/// Represents a timesheet entry returned by the API.
/// </summary>
public record TimesheetEntry(
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

/// <summary>
/// Request body for updating verification status.
/// </summary>
public record VerificationUpdate(string Status, string? Notes);

/// <summary>
/// Summary of timesheet entries for a date range.
/// </summary>
public record TimesheetSummary(
    string UserId,
    string StartDate,
    string EndDate,
    long TotalEntries,
    decimal TotalHours,
    decimal BillableHours,
    long CompliantCount,
    long NonCompliantCount,
    long PendingCount);

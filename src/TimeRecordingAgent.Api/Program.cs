using Npgsql;
using System.Text.Json;
using TimeRecordingAgent.Api;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var connectionString = Environment.GetEnvironmentVariable("PostgreSQL__ConnectionString") 
    ?? builder.Configuration.GetConnectionString("PostgreSQL");
var apiKey = Environment.GetEnvironmentVariable("API_KEY");

// API Key validation middleware - skip for openapi.json and health
// Also skip auth if API_KEY is not set (allows testing without auth)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";
    if (path == "/health" || path == "/openapi.json")
    {
        await next();
        return;
    }
    
    // Only enforce auth if API_KEY environment variable is set
    if (!string.IsNullOrEmpty(apiKey))
    {
        if (!context.Request.Headers.TryGetValue("x-api-key", out var providedKey) || providedKey != apiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid or missing API key");
            return;
        }
    }
    await next();
});

// OpenAPI schema endpoint
app.MapGet("/openapi.json", () => Results.Content(OpenApi.Schema, "application/json"));

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Get timesheet entries for a user on a specific date
app.MapGet("/api/timesheets/{userId}/{date}", async (string userId, string date) =>
{
    if (!DateOnly.TryParse(date, out var parsedDate))
        return Results.BadRequest("Invalid date format. Use yyyy-MM-dd");

    try
    {
        var entries = await GetEntriesAsync(connectionString!, userId, parsedDate);
        return Results.Ok(entries);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

// Get unverified entries for a user
app.MapGet("/api/timesheets/{userId}/unverified", async (string userId) =>
{
    try
    {
        var entries = await GetUnverifiedEntriesAsync(connectionString!, userId);
        return Results.Ok(entries);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

// Get summary for a user
app.MapGet("/api/timesheets/{userId}/summary", async (string userId, string? startDate, string? endDate) =>
{
    var start = DateOnly.TryParse(startDate, out var s) ? s : DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
    var end = DateOnly.TryParse(endDate, out var e) ? e : DateOnly.FromDateTime(DateTime.Today);

    try
    {
        var summary = await GetSummaryAsync(connectionString!, userId, start, end);
        return Results.Ok(summary);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

// Update verification status
app.MapPost("/api/timesheets/verify/{entryId}", async (long entryId, VerificationUpdate update) =>
{
    if (string.IsNullOrEmpty(update.Status))
        return Results.BadRequest("Request body must include 'status' field");

    try
    {
        await UpdateVerificationAsync(connectionString!, entryId, update.Status, update.Notes);
        return Results.Ok($"Entry {entryId} verification updated to '{update.Status}'");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

app.Run();

// --- Database Functions ---

static async Task<List<TimesheetEntry>> GetEntriesAsync(string connectionString, string userId, DateOnly date)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    var dateStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    var dateEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    await using var command = new NpgsqlCommand(@"
        SELECT id, local_id, user_id, started_at, ended_at, duration_hours,
               process_name, document_name, window_title, group_name,
               is_billable, billable_category, description, is_approved,
               verification_status, verification_notes, synced_at
        FROM timesheet_entries
        WHERE user_id = @userId AND started_at >= @dateStart AND started_at < @dateEnd
        ORDER BY started_at", connection);

    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("dateStart", dateStart);
    command.Parameters.AddWithValue("dateEnd", dateEnd);

    return await ReadEntriesAsync(command);
}

static async Task<List<TimesheetEntry>> GetUnverifiedEntriesAsync(string connectionString, string userId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(@"
        SELECT id, local_id, user_id, started_at, ended_at, duration_hours,
               process_name, document_name, window_title, group_name,
               is_billable, billable_category, description, is_approved,
               verification_status, verification_notes, synced_at
        FROM timesheet_entries
        WHERE user_id = @userId AND (verification_status IS NULL OR verification_status = 'Pending')
        ORDER BY started_at DESC LIMIT 100", connection);

    command.Parameters.AddWithValue("userId", userId);

    return await ReadEntriesAsync(command);
}

static async Task<List<TimesheetEntry>> ReadEntriesAsync(NpgsqlCommand command)
{
    var results = new List<TimesheetEntry>();
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        results.Add(new TimesheetEntry(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetDateTime(3), reader.GetDateTime(4), reader.GetDecimal(5),
            reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetBoolean(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetBoolean(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetDateTime(16)));
    }

    return results;
}

static async Task<TimesheetSummary> GetSummaryAsync(string connectionString, string userId, DateOnly startDate, DateOnly endDate)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    var dateStart = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    var dateEnd = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    await using var command = new NpgsqlCommand(@"
        SELECT COUNT(*), COALESCE(SUM(duration_hours), 0),
               COALESCE(SUM(CASE WHEN is_billable THEN duration_hours ELSE 0 END), 0),
               SUM(CASE WHEN verification_status = 'Compliant' THEN 1 ELSE 0 END),
               SUM(CASE WHEN verification_status = 'NonCompliant' THEN 1 ELSE 0 END),
               SUM(CASE WHEN verification_status IS NULL OR verification_status = 'Pending' THEN 1 ELSE 0 END)
        FROM timesheet_entries
        WHERE user_id = @userId AND started_at >= @dateStart AND started_at < @dateEnd", connection);

    command.Parameters.AddWithValue("userId", userId);
    command.Parameters.AddWithValue("dateStart", dateStart);
    command.Parameters.AddWithValue("dateEnd", dateEnd);

    await using var reader = await command.ExecuteReaderAsync();
    if (await reader.ReadAsync())
    {
        return new TimesheetSummary(userId, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
            reader.GetInt64(0), reader.GetDecimal(1), reader.GetDecimal(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    return new TimesheetSummary(userId, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), 0, 0, 0, 0, 0, 0);
}

static async Task UpdateVerificationAsync(string connectionString, long entryId, string status, string? notes)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand(@"
        UPDATE timesheet_entries SET verification_status = @status, verification_notes = @notes WHERE id = @entryId", connection);

    command.Parameters.AddWithValue("entryId", entryId);
    command.Parameters.AddWithValue("status", status);
    command.Parameters.AddWithValue("notes", notes ?? (object)DBNull.Value);

    await command.ExecuteNonQueryAsync();
}

// --- Records ---

record TimesheetEntry(long Id, long LocalId, string UserId, DateTime StartedAt, DateTime EndedAt,
    decimal DurationHours, string ProcessName, string DocumentName, string? WindowTitle,
    string? GroupName, bool IsBillable, string? BillableCategory, string? Description,
    bool IsApproved, string? VerificationStatus, string? VerificationNotes, DateTime SyncedAt);

record VerificationUpdate(string Status, string? Notes);

record TimesheetSummary(string UserId, string StartDate, string EndDate, long TotalEntries,
    decimal TotalHours, decimal BillableHours, long CompliantCount, long NonCompliantCount, long PendingCount);

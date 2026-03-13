using Npgsql;

var connectionString = args.Length > 0 ? args[0] 
    : "Host=pttimerecordingswecentraldb.postgres.database.azure.com;Database=timesheets;Username=dbadminuser;Password=Tamworth1!;SSL Mode=Require";

Console.WriteLine("Connecting to PostgreSQL...");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
Console.WriteLine($"Connected to PostgreSQL {connection.ServerVersion}");

Console.WriteLine("Creating timesheet_entries table...");

await using var cmd = connection.CreateCommand();
cmd.CommandText = """
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
    """;

await cmd.ExecuteNonQueryAsync();
Console.WriteLine("✅ Table and indexes created successfully!");

// Verify
await using var verifyCmd = connection.CreateCommand();
verifyCmd.CommandText = "SELECT COUNT(*) FROM timesheet_entries";
var count = await verifyCmd.ExecuteScalarAsync();
Console.WriteLine($"Table has {count} rows.");

using Microsoft.Data.Sqlite;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TimeRecordingAgent",
    "time-tracking.db");

// Check if first arg looks like a path (contains .db)
if (args.Length > 0 && args[0].EndsWith(".db", StringComparison.OrdinalIgnoreCase))
{
    dbPath = args[0];
    args = args.Skip(1).ToArray();
}

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found at: {dbPath}");
    return 1;
}

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();
using var command = connection.CreateCommand();

// Use custom SQL if provided, otherwise default query
command.CommandText = args.Length > 0 
    ? string.Join(" ", args)
    : "SELECT id, process_name, document_name, started_at FROM activity_log ORDER BY id DESC LIMIT 20";
    
using var reader = command.ExecuteReader();

// Print column headers
var columnCount = reader.FieldCount;
var headers = Enumerable.Range(0, columnCount).Select(i => reader.GetName(i));
Console.WriteLine(string.Join(" | ", headers));
Console.WriteLine(new string('-', 100));

while (reader.Read())
{
    var values = Enumerable.Range(0, columnCount).Select(i => reader.IsDBNull(i) ? "<null>" : reader.GetValue(i)?.ToString() ?? "");
    Console.WriteLine(string.Join(" | ", values));
}

return 0;


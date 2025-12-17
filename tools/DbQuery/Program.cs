using Microsoft.Data.Sqlite;

var dbPath = args.Length > 0 ? args[0] : @"C:\Users\paultozer\TimeRecordingAgent\src\TimeRecordingAgent.App\bin\Debug\net8.0-windows\data\time-tracking.db";
using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();
using var command = connection.CreateCommand();
command.CommandText = "SELECT id, process_name, document_name, started_at FROM activity_log ORDER BY id DESC LIMIT 20";
using var reader = command.ExecuteReader();
Console.WriteLine("ID | Process | Document | Started");
Console.WriteLine(new string('-', 80));
while (reader.Read())
{
    Console.WriteLine($"{reader.GetInt64(0)} | {reader.GetString(1)} | {reader.GetString(2)} | {reader.GetString(3)}");
}


using Microsoft.Data.Sqlite;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Denials;

public sealed class SqliteAccessDenialStore : IAccessDenialStore
{
    private readonly string _connectionString;
    public SqliteAccessDenialStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (databasePath.Contains('=', StringComparison.Ordinal))
            databasePath = new SqliteConnectionStringBuilder(databasePath).DataSource;
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS access_denials (event_id TEXT PRIMARY KEY, subject TEXT NOT NULL, occurred_at TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_access_denials_subject_time ON access_denials(subject, occurred_at);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task RecordAsync(string subject, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO access_denials(event_id, subject, occurred_at) VALUES ($id, $subject, $time);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$time", occurredAt.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<AccessDenialEvent>> GetWindowAsync(string subject, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT event_id, subject, occurred_at FROM access_denials WHERE subject = $subject AND occurred_at >= $since ORDER BY occurred_at;";
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$since", since.UtcDateTime.ToString("O"));
        var events = new List<AccessDenialEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) events.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        return events;
    }
    public async Task<int> DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM access_denials WHERE occurred_at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

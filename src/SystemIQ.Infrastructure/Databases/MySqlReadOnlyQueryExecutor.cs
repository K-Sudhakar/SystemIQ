using System.Data;
using System.Text.Json;
using MySqlConnector;
using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlReadOnlyQueryExecutor(MySqlConnectionFactory connections) : IReadOnlyQueryExecutor
{
    public async Task<QueryRows> ExecuteAsync(ValidatedSql query, DatabaseConnection connection, QueryExecutionLimits limits, CancellationToken cancellationToken)
    {
        if (limits.MaxRows <= 0 || limits.MaxBytes <= 0 || limits.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(limits));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.Timeout);
        await using var db = await connections.OpenAsync(connection, timeout.Token);
        await using (var readOnly = db.CreateCommand())
        {
            readOnly.CommandText = "SET TRANSACTION READ ONLY";
            readOnly.CommandTimeout = TimeoutSeconds(limits.Timeout);
            await readOnly.ExecuteNonQueryAsync(timeout.Token);
        }
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, timeout.Token);
        await using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query.Text;
        command.CommandTimeout = TimeoutSeconds(limits.Timeout);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        long bytes = 0;
        var truncated = false;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, timeout.Token);
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        while (await reader.ReadAsync(timeout.Token))
        {
            if (rows.Count >= limits.MaxRows) { truncated = true; break; }
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++) row[names[i]] = await reader.IsDBNullAsync(i, timeout.Token) ? null : Normalize(reader.GetValue(i));
            var rowBytes = JsonSerializer.SerializeToUtf8Bytes(row).LongLength;
            if (bytes + rowBytes > limits.MaxBytes) { truncated = true; break; }
            bytes += rowBytes;
            rows.Add(row);
        }
        await reader.DisposeAsync();
        await transaction.CommitAsync(timeout.Token);
        return new QueryRows(names, rows, truncated, bytes);
    }
    private static int TimeoutSeconds(TimeSpan timeout) => Math.Max(1, checked((int)Math.Ceiling(timeout.TotalSeconds)));
    private static object Normalize(object value) => value switch
    {
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O"),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using SystemIQ.Application.Databases;
using SystemIQ.Domain.Databases;

namespace SystemIQ.Infrastructure.Databases;

public sealed class MySqlSchemaIntrospector(MySqlConnectionFactory connections) : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        await using var db = await connections.OpenAsync(connection, cancellationToken);
        var catalog = db.Database;
        var columns = new List<ColumnRow>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = """
                SELECT c.TABLE_NAME, c.COLUMN_NAME, c.COLUMN_TYPE, c.IS_NULLABLE, c.ORDINAL_POSITION,
                       CASE WHEN tc.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 1 ELSE 0 END AS IS_PRIMARY_KEY
                FROM information_schema.COLUMNS c
                LEFT JOIN information_schema.KEY_COLUMN_USAGE k
                  ON k.TABLE_SCHEMA = c.TABLE_SCHEMA AND k.TABLE_NAME = c.TABLE_NAME AND k.COLUMN_NAME = c.COLUMN_NAME AND k.CONSTRAINT_NAME = 'PRIMARY'
                LEFT JOIN information_schema.TABLE_CONSTRAINTS tc
                  ON tc.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND tc.TABLE_NAME = k.TABLE_NAME AND tc.CONSTRAINT_NAME = k.CONSTRAINT_NAME
                WHERE c.TABLE_SCHEMA = @schema
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
                """;
            command.Parameters.AddWithValue("@schema", catalog);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3) == "YES", reader.GetInt32(4), reader.GetInt32(5) == 1));
        }
        var relationships = new List<SchemaRelationship>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = """
                SELECT k.CONSTRAINT_NAME, k.TABLE_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME, k.ORDINAL_POSITION
                FROM information_schema.KEY_COLUMN_USAGE k
                WHERE k.TABLE_SCHEMA = @schema AND k.REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION
                """;
            command.Parameters.AddWithValue("@schema", catalog);
            var rows = new List<RelationshipRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            relationships.AddRange(rows.GroupBy(x => (x.Name, x.FromTable, x.ToTable)).Select(g => new SchemaRelationship(g.Key.Name, g.Key.FromTable, g.Select(x => x.FromColumn).ToArray(), g.Key.ToTable, g.Select(x => x.ToColumn).ToArray())));
        }
        var tables = columns.GroupBy(x => x.Table).Select(group => new SchemaTable(catalog, null, group.Key,
            group.Select(x => new SchemaColumn(x.Name, x.NativeType, x.Nullable, x.Ordinal, x.PrimaryKey)).ToArray())).ToArray();
        var providerVersion = db.ServerVersion;
        var hashInput = JsonSerializer.Serialize(new { catalog, providerVersion, tables, relationships });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        return new SchemaSnapshot("mysql", providerVersion, catalog, null, tables, relationships, hash, DateTimeOffset.UtcNow);
    }
    private sealed record ColumnRow(string Table, string Name, string NativeType, bool Nullable, int Ordinal, bool PrimaryKey);
    private sealed record RelationshipRow(string Name, string FromTable, string FromColumn, string ToTable, string ToColumn);
}

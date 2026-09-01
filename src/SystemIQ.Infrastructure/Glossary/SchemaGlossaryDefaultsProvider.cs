using System.Globalization;
using System.Text.RegularExpressions;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Glossary;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Glossary;

namespace SystemIQ.Infrastructure.Glossary;

public sealed class SchemaGlossaryDefaultsProvider(IDatabaseProviderRegistry databases) : IGlossaryDefaultsProvider
{
    public async Task<IReadOnlyList<GlossaryEntry>> GetAsync(
        DatabaseConnection connection,
        IReadOnlySet<string> authorizedObjects,
        int maxEntries,
        CancellationToken cancellationToken)
    {
        var database = databases.GetRequired(connection.ProviderId, DatabaseCapabilities.SchemaDiscovery);
        var schema = await database.Schema.DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
        return schema.Tables
            .Select(table => new { Table = table, Name = QualifiedName(table) })
            .Where(item => GlossaryEntryRules.IsAuthorizedTable(item.Name, authorizedObjects))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxEntries)
            .Select(item => new GlossaryEntry(
                connection.Id,
                item.Name,
                Humanize(item.Table.Name),
                $"Database table {item.Name}.",
                [],
                item.Table.Columns.OrderBy(column => column.Ordinal).Select(column => column.Name).ToArray(),
                schema.Relationships.Where(relationship =>
                        relationship.FromTable.Equals(item.Table.Name, StringComparison.OrdinalIgnoreCase) ||
                        relationship.ToTable.Equals(item.Table.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(relationship => $"{relationship.FromTable}.{string.Join(',', relationship.FromColumns)} -> " +
                        $"{relationship.ToTable}.{string.Join(',', relationship.ToColumns)}").ToArray()))
            .ToArray();
    }

    private static string QualifiedName(SchemaTable table) =>
        string.IsNullOrWhiteSpace(table.Schema) ? table.Name : $"{table.Schema}.{table.Name}";
    private static string Humanize(string value)
    {
        var spaced = Regex.Replace(value.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }
}

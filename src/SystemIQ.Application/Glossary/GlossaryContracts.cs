using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Glossary;

namespace SystemIQ.Application.Glossary;

public interface IGlossaryStore
{
    Task<IReadOnlyList<GlossaryEntry>> ListAsync(
        string subject, string connectionId, int maxEntries, CancellationToken cancellationToken);
    Task<GlossaryEntry> UpsertAsync(string subject, GlossaryEntry entry, CancellationToken cancellationToken);
}

public interface IGlossaryDefaultsProvider
{
    Task<IReadOnlyList<GlossaryEntry>> GetAsync(
        DatabaseConnection connection,
        IReadOnlySet<string> authorizedObjects,
        int maxEntries,
        CancellationToken cancellationToken);
}

public static class GlossaryEntryRules
{
    public const int MaximumIdentifierLength = 256;
    public const int MaximumBusinessTermLength = 200;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumListItems = 100;
    public const int MaximumListItemLength = 500;

    public static string? Validate(GlossaryEntry? entry)
    {
        if (entry is null) return "A glossary entry is required.";
        if (InvalidRequired(entry.ConnectionId, MaximumIdentifierLength)) return "A valid connectionId is required.";
        if (InvalidRequired(entry.Table, MaximumIdentifierLength)) return "A valid table is required.";
        if (InvalidRequired(entry.BusinessTerm, MaximumBusinessTermLength)) return "A valid businessTerm is required.";
        if (InvalidRequired(entry.Description, MaximumDescriptionLength)) return "A valid description is required.";
        if (InvalidList(entry.Synonyms) || InvalidList(entry.RelatedColumns) || InvalidList(entry.JoinHints))
            return "Glossary lists exceed the allowed item count or item length.";
        return null;
    }

    public static bool IsAuthorizedTable(string table, IReadOnlySet<string> authorizedObjects)
    {
        if (authorizedObjects.Contains("*")) return true;
        if (authorizedObjects.Contains(table)) return true;
        var unqualified = table.Split('.').Last();
        return authorizedObjects.Contains(unqualified);
    }

    public static GlossaryEntry Normalize(GlossaryEntry entry, string connectionId) => entry with
    {
        ConnectionId = connectionId,
        Table = entry.Table.Trim(),
        BusinessTerm = entry.BusinessTerm.Trim(),
        Description = entry.Description.Trim(),
        Synonyms = NormalizeList(entry.Synonyms),
        RelatedColumns = NormalizeList(entry.RelatedColumns),
        JoinHints = NormalizeList(entry.JoinHints)
    };

    private static bool InvalidRequired(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl);
    private static bool InvalidList(IReadOnlyList<string>? values) =>
        values is null || values.Count > MaximumListItems || values.Any(value =>
            string.IsNullOrWhiteSpace(value) || value.Length > MaximumListItemLength || value.Any(char.IsControl));
    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string> values) =>
        values.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

using SystemIQ.Domain.Queries;

namespace SystemIQ.Application.Queries;

public static class QueryHistorySelection
{
    public static IReadOnlyList<QueryHistoryEntry> Select(
        IEnumerable<QueryHistoryEntry> entries,
        string subject,
        string connectionId,
        int maxEntries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));

        return entries
            .Where(entry => entry.Subject.Equals(subject, StringComparison.Ordinal) &&
                entry.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CompletedAt)
            .ThenByDescending(entry => entry.CorrelationId, StringComparer.Ordinal)
            .Take(maxEntries)
            .OrderBy(entry => entry.CompletedAt)
            .ThenBy(entry => entry.CorrelationId, StringComparer.Ordinal)
            .ToArray();
    }
}

using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Queries;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemQueryHistorySink(IObjectDocumentStore documents) : IQueryHistorySink, IQueryHistoryReader
{
    public async Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{entry.Subject}|{entry.ConnectionId}|{entry.CorrelationId}"))).ToLowerInvariant();
        await documents.WriteAsync(new DocumentKey("query-history", [id]), entry,
            new WriteCondition(WriteConditionKind.Unconditional), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QueryHistoryEntry>> ReadAsync(
        string subject, string connectionId, int maxEntries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        cancellationToken.ThrowIfCancellationRequested();

        var latest = new PriorityQueue<QueryHistoryEntry, (long CompletedTicks, string CorrelationId)>();
        await foreach (var document in documents.ListAsync<QueryHistoryEntry>(
            new DocumentPrefix("query-history", []), cancellationToken).ConfigureAwait(false))
        {
            var entry = document.Value;
            if (!entry.Subject.Equals(subject, StringComparison.Ordinal) ||
                !entry.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase))
                continue;

            latest.Enqueue(entry, (entry.CompletedAt.UtcDateTime.Ticks, entry.CorrelationId));
            if (latest.Count > maxEntries) latest.Dequeue();
        }

        return QueryHistorySelection.Select(
            latest.UnorderedItems.Select(item => item.Element), subject, connectionId, maxEntries);
    }
}

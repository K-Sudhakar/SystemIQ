using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Queries;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemQueryHistorySink(IObjectDocumentStore documents) : IQueryHistorySink
{
    public async Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{entry.Subject}|{entry.ConnectionId}|{entry.CorrelationId}"))).ToLowerInvariant();
        await documents.WriteAsync(new DocumentKey("query-history", [id]), entry,
            new WriteCondition(WriteConditionKind.Unconditional), cancellationToken).ConfigureAwait(false);
    }
}

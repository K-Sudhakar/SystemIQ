using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Feedback;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemFeedbackSink(IObjectDocumentStore documents) : IFeedbackSink
{
    public async Task SaveAsync(FeedbackEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var identity = $"{entry.Subject.Length}:{entry.Subject}" +
            $"{entry.ConnectionId.Length}:{entry.ConnectionId}" +
            $"{entry.MessageId.Length}:{entry.MessageId}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        await documents.WriteAsync(new DocumentKey("query-feedback", [id]), entry,
            new WriteCondition(WriteConditionKind.Unconditional), cancellationToken).ConfigureAwait(false);
    }
}

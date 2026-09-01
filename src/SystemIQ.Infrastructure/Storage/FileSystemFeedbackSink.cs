using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Feedback;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemFeedbackSink(IObjectDocumentStore documents) : IFeedbackSink, IFeedbackReviewStore
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

    public async Task<IReadOnlyList<FeedbackReviewEntry>> ListPendingAsync(
        string subject, IReadOnlySet<string> connectionIds, string? connectionId,
        int maxEntries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(connectionIds);
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var result = new List<FeedbackReviewEntry>();
        await foreach (var document in documents.ListAsync<FeedbackReviewEntry>(
            new DocumentPrefix("feedback-reviews", []), cancellationToken).ConfigureAwait(false))
        {
            var review = document.Value;
            if (!review.Resolved && review.Subject.Equals(subject, StringComparison.Ordinal) &&
                connectionIds.Contains(review.ConnectionId) &&
                (string.IsNullOrWhiteSpace(connectionId) || review.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase)))
                result.Add(review);
        }
        return result.OrderBy(review => review.CreatedAt).ThenBy(review => review.Id, StringComparer.Ordinal)
            .Take(maxEntries).ToArray();
    }

    public async Task<int> ProcessAsync(
        string subject, IReadOnlySet<string> connectionIds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(connectionIds);
        var questions = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var document in documents.ListAsync<QueryHistoryEntry>(
            new DocumentPrefix("query-history", []), cancellationToken).ConfigureAwait(false))
        {
            var history = document.Value;
            if (history.Subject.Equals(subject, StringComparison.Ordinal) && connectionIds.Contains(history.ConnectionId))
                questions[HistoryKey(history.ConnectionId, history.CorrelationId)] = history.Question;
        }

        var processed = 0;
        await foreach (var document in documents.ListAsync<FeedbackEntry>(
            new DocumentPrefix("query-feedback", []), cancellationToken).ConfigureAwait(false))
        {
            var entry = document.Value;
            if (!entry.Subject.Equals(subject, StringComparison.Ordinal) || !connectionIds.Contains(entry.ConnectionId) || entry.Rating != "down")
                continue;
            var id = document.Key.Segments.Single();
            var key = new DocumentKey("feedback-reviews", [id]);
            if (await documents.ReadAsync<FeedbackReviewEntry>(key, cancellationToken).ConfigureAwait(false) is not null)
                continue;
            questions.TryGetValue(HistoryKey(entry.ConnectionId, entry.MessageId), out var question);
            var review = new FeedbackReviewEntry(id, entry.Subject, entry.ConnectionId, entry.MessageId,
                question ?? string.Empty, [], [], entry.Reason, entry.Comment, entry.CreatedAt, false);
            await documents.WriteAsync(key, review, new WriteCondition(WriteConditionKind.CreateOnly),
                cancellationToken).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }

    public async Task<bool> ResolveAsync(
        string subject, IReadOnlySet<string> connectionIds, string id, CancellationToken cancellationToken)
    {
        if (id.Length != 64 || id.Any(character => !char.IsAsciiHexDigit(character))) return false;
        var key = new DocumentKey("feedback-reviews", [id]);
        var document = await documents.ReadAsync<FeedbackReviewEntry>(key, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.Value.Subject.Equals(subject, StringComparison.Ordinal) ||
            !connectionIds.Contains(document.Value.ConnectionId)) return false;
        await documents.WriteAsync(key, document.Value with { Resolved = true },
            new WriteCondition(WriteConditionKind.MatchVersion, document.Version), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string HistoryKey(string connectionId, string messageId) =>
        $"{connectionId.ToUpperInvariant()}\u001f{messageId}";
}

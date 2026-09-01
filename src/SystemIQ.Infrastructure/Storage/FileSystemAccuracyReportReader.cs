using SystemIQ.Application.Feedback;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemAccuracyReportReader(IObjectDocumentStore documents) : IAccuracyReportReader
{
    public async Task<AccuracyReport> ReadAsync(
        string subject, IReadOnlySet<string> connectionIds, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(connectionIds);
        var answers = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await foreach (var document in documents.ListAsync<QueryHistoryEntry>(
            new DocumentPrefix("query-history", []), cancellationToken).ConfigureAwait(false))
        {
            var history = document.Value;
            if (history.Subject.Equals(subject, StringComparison.Ordinal) && connectionIds.Contains(history.ConnectionId) &&
                history.CompletedAt >= since)
                answers[Key(history.ConnectionId, history.CorrelationId)] = history.CompletedAt;
        }

        var ratings = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var document in documents.ListAsync<FeedbackEntry>(
            new DocumentPrefix("query-feedback", []), cancellationToken).ConfigureAwait(false))
        {
            var feedback = document.Value;
            var key = Key(feedback.ConnectionId, feedback.MessageId);
            if (feedback.Subject.Equals(subject, StringComparison.Ordinal) && connectionIds.Contains(feedback.ConnectionId) &&
                feedback.CreatedAt >= since && answers.ContainsKey(key) && feedback.Rating is "up" or "down")
                ratings[key] = feedback.Rating;
        }

        var answerCount = answers.Count;
        var ratedCount = ratings.Count;
        var up = ratings.Count(pair => pair.Value == "up");
        var down = ratedCount - up;
        return new AccuracyReport(
            Percent(up, ratedCount),
            Percent(down, ratedCount),
            Percent(ratedCount, answerCount),
            answerCount,
            ratedCount,
            answerCount == 0 ? null : answers.Values.Min(),
            answerCount == 0 ? null : answers.Values.Max());
    }

    private static double Percent(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(100d * numerator / denominator, 2, MidpointRounding.AwayFromZero);
    private static string Key(string connectionId, string messageId) =>
        $"{connectionId.ToUpperInvariant()}\u001f{messageId}";
}

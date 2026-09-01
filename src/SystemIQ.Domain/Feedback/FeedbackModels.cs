namespace SystemIQ.Domain.Feedback;

public sealed record FeedbackEntry(
    string Subject,
    string ConnectionId,
    string MessageId,
    string Rating,
    string? Reason,
    string? Comment,
    DateTimeOffset CreatedAt);

public sealed record FeedbackReviewEntry(
    string Id,
    string Subject,
    string ConnectionId,
    string MessageId,
    string Question,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchedTables,
    string? Reason,
    string? Comment,
    DateTimeOffset CreatedAt,
    bool Resolved);

public sealed record AccuracyReport(
    double ThumbsUpRate,
    double ThumbsDownRate,
    double FeedbackCoverage,
    int AnswerCount,
    int RatedCount,
    DateTimeOffset? From,
    DateTimeOffset? To);

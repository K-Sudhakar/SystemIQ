namespace SystemIQ.Domain.Feedback;

public sealed record FeedbackEntry(
    string Subject,
    string ConnectionId,
    string MessageId,
    string Rating,
    string? Reason,
    string? Comment,
    DateTimeOffset CreatedAt);

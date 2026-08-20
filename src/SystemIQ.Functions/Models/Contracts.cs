using System.Text.Json.Serialization;

namespace SystemIQ.Functions.Models;

public sealed record ConnectionSummary(string Id, string DisplayName);

public sealed record ChatRequest(string ConnectionId, string Question);

public sealed record FeedbackRequest(
    string ConnectionId,
    string MessageId,
    string Rating,
    string? Reason,
    string? Comment);

public sealed record FeedbackInfo(string Rating, string? Reason, string? Comment, DateTimeOffset CreatedAt);

public sealed record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows = null,
    FeedbackInfo? Feedback = null,
    IReadOnlyList<string>? MatchedTerms = null,
    IReadOnlyList<string>? MatchedTables = null,
    [property: JsonIgnore] bool IncludeInModelContext = true);

public sealed record GlossaryEntry(
    string ConnectionId,
    string Table,
    string BusinessTerm,
    string Description,
    IReadOnlyList<string> Synonyms,
    IReadOnlyList<string> RelatedColumns,
    IReadOnlyList<string> JoinHints);

public sealed record FeedbackReviewItem(
    string Id,
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

public sealed record AuditLogEntry(
    string UserObjectId,
    string QuestionOrSql,
    string ConnectionId,
    string DenialReason,
    DateTimeOffset Timestamp);

public sealed record AccessPolicy(
    IReadOnlySet<string> Connections,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTables,
    IReadOnlyDictionary<string, IReadOnlySet<string>> DeniedColumns);

public sealed record ChatResult(
    string MessageId,
    string Answer,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchedTables);

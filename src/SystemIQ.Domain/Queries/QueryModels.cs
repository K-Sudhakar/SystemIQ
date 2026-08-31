using SystemIQ.Domain.Databases;

namespace SystemIQ.Domain.Queries;

public sealed record NaturalLanguageQueryRequest(
    string Question, DatabaseConnection Connection, string ChatProfile, string EmbeddingProfile,
    IReadOnlySet<string> AuthorizedObjects, string CorrelationId, int RagTopK = 12,
    int RagTokenBudget = 3000, string Subject = "");
public enum QueryFailureCode { InvalidRequest, ProviderUnavailable, RetrievalFailed, SqlGenerationFailed, SqlRejected, ExecutionFailed, AnswerGenerationFailed, PersistenceFailed, Cancelled }
public sealed record QueryFailure(QueryFailureCode Code, string Message, bool IsRetryable = false);
public sealed record QueryEvaluationMetadata(
    string ProviderId, string SchemaHash, string IndexVersion, bool RetrievalDegraded,
    int RetrievedChunkCount, TokenUsageSummary Tokens);
public sealed record TokenUsageSummary(int InputTokens, int OutputTokens);
public sealed record NaturalLanguageQueryResult(
    bool IsSuccess, string? Answer, QueryRows? Rows, string? Sql, QueryFailure? Failure, QueryEvaluationMetadata? Evaluation)
{
    public static NaturalLanguageQueryResult Failed(QueryFailure failure) => new(false, null, null, null, failure, null);
}
public sealed record QueryHistoryEntry(
    string CorrelationId, string ConnectionId, string Question, string Answer, string Sql,
    int RowCount, DateTimeOffset CompletedAt, QueryEvaluationMetadata Evaluation, string Subject = "");

using System.Text;
using SystemIQ.Application.AI;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Application.Queries;

public sealed class NaturalLanguageQueryOrchestrator(
    IDatabaseProviderRegistry databases,
    IChatProviderRegistry chats,
    IRagRetriever retriever,
    IQueryHistorySink history,
    QueryExecutionLimits? executionLimits = null) : INaturalLanguageQueryOrchestrator
{
    private const DatabaseCapabilities RequiredCapabilities = DatabaseCapabilities.SchemaDiscovery |
        DatabaseCapabilities.DialectRendering | DatabaseCapabilities.SqlValidation | DatabaseCapabilities.ReadOnlyExecution;
    private readonly QueryExecutionLimits _executionLimits = executionLimits ?? QueryExecutionLimits.Default;

    public async Task<NaturalLanguageQueryResult> ExecuteAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4000)
            return NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.InvalidRequest, "Question must contain between 1 and 4,000 characters."));

        var failureCode = QueryFailureCode.ProviderUnavailable;
        try
        {
            var database = databases.GetRequired(request.Connection.ProviderId, RequiredCapabilities);
            var chat = chats.GetRequired(request.ChatProfile);
            var schema = await database.Schema.DiscoverAsync(request.Connection, cancellationToken).ConfigureAwait(false);
            failureCode = QueryFailureCode.RetrievalFailed;
            var retrieval = await retriever.RetrieveAsync(new RagRetrievalRequest(
                request.Question, request.Connection.Id, request.EmbeddingProfile, request.AuthorizedObjects,
                request.RagTopK, request.RagTokenBudget), cancellationToken).ConfigureAwait(false);

            failureCode = QueryFailureCode.SqlGenerationFailed;
            var sqlCompletion = await chat.CompleteAsync(CreateSqlRequest(request, database.Dialect, schema, retrieval), cancellationToken).ConfigureAwait(false);
            failureCode = QueryFailureCode.SqlRejected;
            var validation = await database.Validator.ValidateAsync(sqlCompletion.Content,
                new SqlValidationContext(database.ProviderId, request.AuthorizedObjects, _executionLimits.MaxRows,
                    AllowedCatalog: schema.Catalog), cancellationToken).ConfigureAwait(false);
            if (!validation.IsAllowed || validation.Query is null)
                return NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.SqlRejected,
                    validation.RejectionReason ?? "Generated SQL was rejected by the safety policy."));

            failureCode = QueryFailureCode.ExecutionFailed;
            var rows = await database.Executor.ExecuteAsync(validation.Query, request.Connection, _executionLimits, cancellationToken).ConfigureAwait(false);
            failureCode = QueryFailureCode.AnswerGenerationFailed;
            var answerCompletion = await chat.CompleteAsync(CreateAnswerRequest(request, validation.Query, rows), cancellationToken).ConfigureAwait(false);
            var evaluation = new QueryEvaluationMetadata(database.ProviderId, schema.SnapshotHash, retrieval.IndexVersion,
                retrieval.IsDegraded, retrieval.Chunks.Count,
                new TokenUsageSummary(sqlCompletion.Usage.InputTokens + answerCompletion.Usage.InputTokens,
                    sqlCompletion.Usage.OutputTokens + answerCompletion.Usage.OutputTokens));
            var result = new NaturalLanguageQueryResult(true, answerCompletion.Content, rows, validation.Query.Text, null, evaluation);

            failureCode = QueryFailureCode.PersistenceFailed;
            await history.SaveAsync(new QueryHistoryEntry(request.CorrelationId, request.Connection.Id, request.Question,
                answerCompletion.Content, validation.Query.Text, rows.Rows.Count, DateTimeOffset.UtcNow, evaluation,
                request.Subject), cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.Cancelled, "The query was cancelled."));
        }
        catch (ProviderException ex)
        {
            return NaturalLanguageQueryResult.Failed(new QueryFailure(MapProviderFailure(ex.Category), ex.Message, ex.IsRetryable));
        }
        catch (UnknownProviderException ex)
        {
            return NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.ProviderUnavailable, ex.Message));
        }
        catch (MissingDatabaseCapabilityException ex)
        {
            return NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.ProviderUnavailable, ex.Message));
        }
        catch (Exception)
        {
            return NaturalLanguageQueryResult.Failed(new QueryFailure(failureCode, "The query pipeline could not complete this stage."));
        }
    }

    private static ChatRequest CreateSqlRequest(NaturalLanguageQueryRequest request, ISqlDialect dialect,
        SchemaSnapshot schema, RagRetrievalResult retrieval)
    {
        var context = new StringBuilder();
        foreach (var chunk in retrieval.Chunks) context.AppendLine(chunk.Text);
        return new ChatRequest([
            new ChatMessage(
    ChatRole.System,
    $"Generate exactly one read-only SQL query. " +
    $"Return ONLY the raw SQL statement. " +
    $"Do not use Markdown code fences. " +
    $"Do not include explanations, comments, labels, or any text before or after the SQL. " +
    $"The response must begin with SELECT or WITH and contain exactly one statement. " +
    $"{dialect.BuildSqlGenerationGuidance(schema, retrieval.Chunks)}"
),
            new ChatMessage(ChatRole.User, $"Authorized schema and glossary context:\n{context}\nQuestion: {request.Question}")
        ], ChatPurpose.SqlGeneration, request.CorrelationId);
    }

    private static ChatRequest CreateAnswerRequest(NaturalLanguageQueryRequest request, ValidatedSql query, QueryRows rows)
    {
        var boundedRows = string.Join('\n', rows.Rows.Select(row => string.Join(", ", row.Select(cell => $"{cell.Key}={cell.Value}"))));
        return new ChatRequest([
            new ChatMessage(ChatRole.System, "Answer only from the supplied query result. State clearly when no rows were returned."),
            new ChatMessage(ChatRole.User, $"Question: {request.Question}\nValidated query: {query.Text}\nColumns: {string.Join(", ", rows.Columns)}\nRows:\n{boundedRows}")
        ], ChatPurpose.GroundedAnswer, request.CorrelationId);
    }

    private static QueryFailureCode MapProviderFailure(ProviderErrorCategory category) => category switch
    {
        ProviderErrorCategory.Policy => QueryFailureCode.SqlGenerationFailed,
        _ => QueryFailureCode.ProviderUnavailable
    };
}

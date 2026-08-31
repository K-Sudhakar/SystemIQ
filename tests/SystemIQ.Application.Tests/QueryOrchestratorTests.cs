using SystemIQ.Application.AI;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Queries;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Application.Tests;

public sealed class QueryOrchestratorTests
{
    [Fact]
    public async Task Executes_only_validated_sql_and_returns_rows_with_grounded_answer()
    {
        var events = new List<string>();
        var chat = new SequenceChatProvider(events, ["SELECT `name` FROM `customers`", "Ada is the only customer."]);
        var database = new RecordingDatabaseProvider(events, allow: true);
        var history = new RecordingHistory();
        var orchestrator = Create(chat, database, history, events);

        var result = await orchestrator.ExecuteAsync(Request(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada is the only customer.", result.Answer);
        Assert.Single(result.Rows!.Rows);
        Assert.Equal(["schema", "rag", "chat:sql", "validate", "execute", "chat:answer"], events);
        Assert.Equal("SELECT `name` FROM `customers` LIMIT 500", database.ExecutedSql);
        Assert.NotNull(history.Saved);
    }

    [Fact]
    public async Task Validation_denial_prevents_execution_and_history()
    {
        var events = new List<string>();
        var database = new RecordingDatabaseProvider(events, allow: false);
        var history = new RecordingHistory();
        var orchestrator = Create(new SequenceChatProvider(events, ["DROP TABLE customers"]), database, history, events);

        var result = await orchestrator.ExecuteAsync(Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(QueryFailureCode.SqlRejected, result.Failure!.Code);
        Assert.Null(database.ExecutedSql);
        Assert.Null(history.Saved);
    }

    [Fact]
    public async Task Execution_failure_is_normalized_and_does_not_save_history()
    {
        var events = new List<string>();
        var database = new RecordingDatabaseProvider(events, allow: true) { ThrowOnExecute = true };
        var history = new RecordingHistory();
        var orchestrator = Create(new SequenceChatProvider(events, ["SELECT 1"]), database, history, events);

        var result = await orchestrator.ExecuteAsync(Request(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(QueryFailureCode.ExecutionFailed, result.Failure!.Code);
        Assert.Null(history.Saved);
    }

    [Fact]
    public async Task Uses_configured_execution_limits_for_validation_and_execution()
    {
        var events = new List<string>();
        var database = new RecordingDatabaseProvider(events, allow: true);
        var limits = new QueryExecutionLimits(25, 2048, TimeSpan.FromSeconds(4));
        var orchestrator = new NaturalLanguageQueryOrchestrator(
            new DatabaseProviderRegistry([database]),
            new ChatProviderRegistry([new SequenceChatProvider(events,
                ["SELECT `name` FROM `customers`", "Ada"])]),
            new StubRetriever(events), new RecordingHistory(), limits);

        var result = await orchestrator.ExecuteAsync(Request(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(25, database.ValidatedMaxRows);
        Assert.Equal(limits, database.ExecutionLimits);
    }

    private static NaturalLanguageQueryOrchestrator Create(IChatCompletionProvider chat, IDatabaseProvider database, IQueryHistorySink history, List<string> events) =>
        new(new DatabaseProviderRegistry([database]), new ChatProviderRegistry([chat]), new StubRetriever(events), history);

    private static NaturalLanguageQueryRequest Request() => new(
        "Who are the customers?", new DatabaseConnection("c1", "Customers", "mysql", "config:db", new Dictionary<string, string>()),
        "chat", "embed", new HashSet<string>(["customers"]), "corr-1", 2, 100);

    private sealed class StubRetriever(List<string> events) : IRagRetriever
    {
        public Task<RagRetrievalResult> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken)
        {
            events.Add("rag");
            return Task.FromResult(new RagRetrievalResult(
                [new RagContextChunk("chunk", "c1", RagSourceType.Table, "customers", "customers(name)", 1)], false, "index-1"));
        }
    }

    private sealed class SequenceChatProvider(List<string> events, Queue<string> responses) : IChatCompletionProvider
    {
        public SequenceChatProvider(List<string> events, IEnumerable<string> responses) : this(events, new Queue<string>(responses)) { }
        public string ProviderId => "chat";
        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            events.Add(request.Purpose == ChatPurpose.SqlGeneration ? "chat:sql" : "chat:answer");
            return Task.FromResult(new ChatCompletion(responses.Dequeue(), "model", new TokenUsage(1, 1), "done"));
        }
        public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.CompletedTask; yield break; }
    }

    private sealed class RecordingDatabaseProvider(List<string> events, bool allow) : IDatabaseProvider, ISchemaIntrospector, ISqlDialect, ISqlValidator, IReadOnlyQueryExecutor
    {
        public string ProviderId => "mysql";
        public DatabaseCapabilities Capabilities => DatabaseCapabilities.All;
        public ISchemaIntrospector Schema => this;
        public ISqlDialect Dialect => this;
        public ISqlValidator Validator => this;
        public IReadOnlyQueryExecutor Executor => this;
        public string? ExecutedSql { get; private set; }
        public int? ValidatedMaxRows { get; private set; }
        public QueryExecutionLimits? ExecutionLimits { get; private set; }
        public bool ThrowOnExecute { get; init; }
        public Task<SchemaSnapshot> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken)
        { events.Add("schema"); return Task.FromResult(new SchemaSnapshot("mysql", "8", "shop", null, [], [], "hash", DateTimeOffset.UtcNow)); }
        public string IdentifierQuote => "`";
        public string BuildSqlGenerationGuidance(SchemaSnapshot schema, IReadOnlyList<RagContextChunk> context) => "MySQL SELECT only";
        public Task<SqlValidationResult> ValidateAsync(string sql, SqlValidationContext context, CancellationToken cancellationToken)
        {
            events.Add("validate");
            ValidatedMaxRows = context.MaxRows;
            return Task.FromResult(allow ? SqlValidationResult.Allow(sql + $" LIMIT {context.MaxRows}", []) : SqlValidationResult.Reject("unsafe"));
        }
        public Task<QueryRows> ExecuteAsync(ValidatedSql query, DatabaseConnection connection, QueryExecutionLimits limits, CancellationToken cancellationToken)
        {
            events.Add("execute");
            if (ThrowOnExecute) throw new InvalidOperationException("database details must not leak");
            ExecutedSql = query.Text;
            ExecutionLimits = limits;
            return Task.FromResult(new QueryRows(["name"], [new Dictionary<string, object?> { ["name"] = "Ada" }], false, 20));
        }
    }

    private sealed class RecordingHistory : IQueryHistorySink
    {
        public QueryHistoryEntry? Saved { get; private set; }
        public Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken) { Saved = entry; return Task.CompletedTask; }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using SystemIQ.Application.AI;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Rag;
using SystemIQ.Infrastructure.AI;
using SystemIQ.Infrastructure.Databases;
using SystemIQ.Infrastructure.Rag;

namespace SystemIQ.Infrastructure.Tests;

public sealed class AiSqlAndRagTests
{
    [Fact]
    public async Task IndependentAiAdapters_UseTheirOwnHostsModelsAndEndpoints()
    {
        var chatHandler = new RecordingHandler("""{"model":"chat-model","choices":[{"message":{"content":"SELECT 1"},"finish_reason":"stop"}],"usage":{"prompt_tokens":2,"completion_tokens":3}}""");
        var embeddingHandler = new RecordingHandler("""{"data":[{"index":0,"embedding":[1,0]}]}""");
        var chat = new OpenAiCompatibleChatProvider(new HttpClient(chatHandler), new("chat", new Uri("https://chat.example/"), "chat-model", TimeSpan.FromSeconds(2), "chat-key"));
        var embedding = new OpenAiCompatibleEmbeddingProvider(new HttpClient(embeddingHandler), new("embed", new Uri("https://embed.example/"), "embed-model", TimeSpan.FromSeconds(2), "embed-key", 2));

        var completion = await chat.CompleteAsync(new([new(ChatRole.User, "question")], ChatPurpose.SqlGeneration, "c"), default);
        var vectors = await embedding.EmbedAsync(["question"], default);

        Assert.Equal("SELECT 1", completion.Content);
        Assert.Equal("https://chat.example/v1/chat/completions", chatHandler.RequestUri!.ToString());
        Assert.Equal("https://embed.example/v1/embeddings", embeddingHandler.RequestUri!.ToString());
        Assert.Contains("chat-model", chatHandler.Body);
        Assert.Contains("embed-model", embeddingHandler.Body);
        Assert.Equal(2, vectors[0].Dimensions);
    }

    [Fact]
    public async Task EmbeddingAdapter_FailsClearlyOnDimensionMismatch()
    {
        var provider = new OpenAiCompatibleEmbeddingProvider(new HttpClient(new RecordingHandler("""{"data":[{"index":0,"embedding":[1]}]}""")),
            new("embed", new Uri("https://embed.example/"), "model", TimeSpan.FromSeconds(2), Dimensions: 2));
        var exception = await Assert.ThrowsAsync<EmbeddingDimensionException>(() => provider.EmbedAsync(["x"], default));
        Assert.Equal(2, exception.ExpectedDimensions);
        Assert.Equal(1, exception.ActualDimensions);
    }

    [Theory]
    [InlineData("INSERT INTO users VALUES (1)")]
    [InlineData("DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("SELECT * FROM users; SELECT * FROM users")]
    [InlineData("SELECT * FROM users FOR UPDATE")]
    [InlineData("SELECT * INTO OUTFILE '/tmp/a' FROM users")]
    [InlineData("SELECT SLEEP(10) FROM users")]
    [InlineData("SELECT * FROM users LIMIT 5000")]
    [InlineData("SELECT * FROM users -- directive")]
    [InlineData("DELETE FROM users LIMIT")]
    [InlineData("SELECT * FROM users; DROP TABLE users LIMIT")]
    public async Task AstValidator_RejectsUnsafeSql(string sql)
    {
        var result = await new MySqlSqlValidator().ValidateAsync(sql, new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users" }, 50), default);
        Assert.False(result.IsAllowed);
    }

    [Theory]
    [InlineData("SELECT department, AVG(salary) AS average_salary FROM employees GROUP BY department ORDER BY average_salary DESC LIMIT 1;")]
    [InlineData("SELECT department, COUNT(*) AS employee_count FROM employees GROUP BY department;")]
    [InlineData("SELECT department, SUM(salary) AS total_salary FROM employees GROUP BY department ORDER BY total_salary DESC;")]
    [InlineData("SELECT MAX(salary) AS highest_salary FROM employees;")]
    [InlineData("SELECT department, AVG(salary) AS average_salary FROM employees GROUP BY department HAVING AVG(salary) > 0 ORDER BY average_salary DESC LIMIT 10 OFFSET 0;")]
    public async Task AstValidator_AllowsReadOnlyAggregateQueries(string sql)
    {
        var result = await new MySqlSqlValidator().ValidateAsync(sql,
            new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "employees" }, 50), default);

        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Theory]
    [InlineData("DELETE FROM employees;")]
    [InlineData("DROP TABLE employees;")]
    [InlineData("SELECT * FROM employees; DROP TABLE employees;")]
    public async Task AstValidator_ContinuesToRejectMutationsAndMultipleStatements(string sql)
    {
        var result = await new MySqlSqlValidator().ValidateAsync(sql,
            new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "employees" }, 50), default);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task AstValidator_ExtractsJoinedObjectsAndAddsBound()
    {
        var result = await new MySqlSqlValidator().ValidateAsync("SELECT u.id FROM users u JOIN orders o ON o.user_id=u.id",
            new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "users", "orders" }, 50), default);
        Assert.True(result.IsAllowed, result.RejectionReason);
        Assert.EndsWith("LIMIT 50", result.Query!.Text);
        Assert.Equal(["users", "orders"], result.Query.ReferencedObjects.Select(x => x.Name));
    }

    [Fact]
    public async Task AstValidator_RejectsCrossCatalogReferenceEvenWithWildcardPolicy()
    {
        var result = await new MySqlSqlValidator().ValidateAsync(
            "SELECT id FROM other_database.users",
            new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }, 50,
                AllowedCatalog: "application_database"), default);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task AstValidator_TreatsExplicitWildcardAsAllowAll()
    {
        var result = await new MySqlSqlValidator().ValidateAsync("SELECT id FROM any_configured_table",
            new("mysql", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }, 50), default);

        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Fact]
    public async Task RagRetriever_FiltersUnauthorizedBeforeRankingAndBoostsExactTerm()
    {
        var compatibility = new RagCompatibility("schema", "glossary", "embed", "model", 2, "content", "index-1");
        var index = new InMemoryRagIndex();
        await index.PublishAsync("c1", [
            Chunk("allowed", "orders", "orders business records", [1, 0], compatibility),
            Chunk("denied", "secrets", "orders secret records", [1, 0], compatibility)
        ], compatibility, default);
        var provider = new StubEmbeddingProvider();
        var retriever = new VectorRagRetriever(index, new EmbeddingProviderRegistry([provider]));

        var result = await retriever.RetrieveAsync(new("show orders", "c1", "embed", new HashSet<string> { "orders" }, 12, 100), default);

        Assert.Single(result.Chunks);
        Assert.Equal("orders", result.Chunks[0].ObjectName);
        Assert.Equal("index-1", result.IndexVersion);
    }

    [Fact]
    public async Task RagRetriever_TreatsWildcardAsAllowAll()
    {
        var compatibility = new RagCompatibility("schema", "glossary", "embed", "model", 2, "content", "index-1");
        var index = new InMemoryRagIndex();
        await index.PublishAsync("c1", [Chunk("one", "orders", "orders", [1, 0], compatibility)], compatibility, default);
        var retriever = new VectorRagRetriever(index, new EmbeddingProviderRegistry([new StubEmbeddingProvider()]));

        var result = await retriever.RetrieveAsync(new("orders", "c1", "embed", new HashSet<string> { "*" }, 12, 100), default);

        Assert.Single(result.Chunks);
    }

    [Fact]
    public void SchemaChunking_IsGenericAndIncludesColumnsPrimaryKeysAndRelationships()
    {
        var schema = new SchemaSnapshot("mysql", "8", "catalog", null,
            [
                new SchemaTable("catalog", null, "authors", [new SchemaColumn("id", "int", false, 1, true)]),
                new SchemaTable("catalog", null, "books", [
                    new SchemaColumn("id", "int", false, 1, true),
                    new SchemaColumn("author_id", "int", false, 2)])
            ],
            [new SchemaRelationship("fk_books_authors", "books", ["author_id"], "authors", ["id"])],
            "snapshot", DateTimeOffset.UtcNow);

        var chunks = SchemaRagIndexer.BuildDefinitions("c1", schema);

        Assert.Contains(chunks, x => x.Type == RagSourceType.Table && x.ObjectName == "books" && x.Text.Contains("Primary key: id"));
        Assert.Contains(chunks, x => x.Type == RagSourceType.Column && x.ObjectName == "books.author_id");
        Assert.Contains(chunks, x => x.Type == RagSourceType.Relationship && x.Text.Contains("join books (author_id) to authors (id)"));
        Assert.DoesNotContain(chunks, x => x.Text.Contains("health", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SchemaChunking_FiltersUnauthorizedTablesBeforeEmbedding()
    {
        var schema = new SchemaSnapshot("mysql", "8", "shop", null,
            [
                new SchemaTable("shop", null, "orders", [new SchemaColumn("id", "int", false, 1, true)]),
                new SchemaTable("shop", null, "payroll", [new SchemaColumn("salary", "decimal", false, 1)])
            ], [new SchemaRelationship("orders_payroll", "orders", ["id"], "payroll", ["salary"])],
            "hash", DateTimeOffset.UtcNow);

        var chunks = SchemaRagIndexer.BuildDefinitions("c1", schema,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "orders" });

        Assert.Contains(chunks, x => x.ObjectName == "orders");
        Assert.DoesNotContain(chunks, x => x.Text.Contains("payroll", StringComparison.OrdinalIgnoreCase));
    }

    private static RagChunk Chunk(string id, string name, string text, float[] values, RagCompatibility compatibility) =>
        new(id, "c1", RagSourceType.Table, name, text, new(values, 2), compatibility, new Dictionary<string, string>());

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderId => "embed";
        public int Dimensions => 2;
        public Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> input, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmbeddingVector>>([new([1, 0], 2)]);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

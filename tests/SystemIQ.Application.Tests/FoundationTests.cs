using System.Collections.ObjectModel;
using SystemIQ.Application.AI;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Queries;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Rag;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Application.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void EmbeddingVector_rejects_dimension_mismatch()
    {
        var exception = Assert.Throws<EmbeddingDimensionException>(
            () => new EmbeddingVector([1f, 2f], expectedDimensions: 3));

        Assert.Equal(3, exception.ExpectedDimensions);
        Assert.Equal(2, exception.ActualDimensions);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("nested/path")]
    [InlineData("nested\\path")]
    [InlineData("file:stream")]
    [InlineData("%2Fetc")]
    public void DocumentKey_rejects_unsafe_segments(string segment)
    {
        Assert.Throws<ArgumentException>(() => new DocumentKey("history", [segment]));
    }

    [Fact]
    public void Provider_registries_are_independent_and_reject_duplicates()
    {
        var chat = new ChatProviderRegistry([new StubChatProvider("shared")]);
        var embeddings = new EmbeddingProviderRegistry([new StubEmbeddingProvider("shared", 3)]);

        Assert.Equal("shared", chat.GetRequired("shared").ProviderId);
        Assert.Equal(3, embeddings.GetRequired("shared").Dimensions);
        Assert.Throws<DuplicateProviderException>(() => new ChatProviderRegistry(
            [new StubChatProvider("duplicate"), new StubChatProvider("DUPLICATE")]));
        Assert.Throws<UnknownProviderException>(() => embeddings.GetRequired("missing"));
    }

    [Fact]
    public void Database_registry_rejects_a_provider_without_required_capabilities()
    {
        var provider = new StubDatabaseProvider(DatabaseCapabilities.SchemaDiscovery);
        var registry = new DatabaseProviderRegistry([provider]);

        var exception = Assert.Throws<MissingDatabaseCapabilityException>(() =>
            registry.GetRequired("mysql", DatabaseCapabilities.ReadOnlyExecution));

        Assert.Equal(DatabaseCapabilities.ReadOnlyExecution, exception.MissingCapabilities);
    }

    [Fact]
    public async Task Retriever_filters_before_ranking_and_prioritizes_exact_terms()
    {
        var chunks = new[]
        {
            Chunk("allowed-exact", "c1", "orders", "Orders contain customer purchases", [0f, 1f]),
            Chunk("allowed-semantic", "c1", "customers", "People who buy products", [1f, 0f]),
            Chunk("denied", "c1", "payroll", "orders payroll secrets", [1f, 0f]),
            Chunk("other-connection", "c2", "orders", "orders from another tenant", [1f, 0f])
        };
        var retriever = new AuthorizedRagRetriever(
            new EmbeddingProviderRegistry([new StubEmbeddingProvider("embed", 2, [1f, 0f])]),
            new StubRagIndex(chunks));

        var result = await retriever.RetrieveAsync(new RagRetrievalRequest(
            "show orders", "c1", "embed", new HashSet<string>(["orders", "customers"]), 2, 100), default);

        Assert.Equal(["allowed-exact", "allowed-semantic"], result.Chunks.Select(c => c.ChunkId));
        Assert.DoesNotContain(result.Chunks, c => c.ObjectName == "payroll" || c.ConnectionId == "c2");
        Assert.False(result.IsDegraded);
    }

    private static RagChunk Chunk(string id, string connection, string objectName, string text, float[] vector) =>
        new(id, connection, RagSourceType.Table, objectName, text, new EmbeddingVector(vector, 2),
            new RagCompatibility("schema-1", "glossary-1", "embed", "model", 2, "content-1", "index-1"),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));

    private sealed class StubEmbeddingProvider(string id, int dimensions, float[]? vector = null) : IEmbeddingProvider
    {
        public string ProviderId => id;
        public int Dimensions => dimensions;
        public Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> input, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmbeddingVector>>(input.Select(_ => new EmbeddingVector(vector ?? new float[dimensions], dimensions)).ToArray());
    }

    private sealed class StubChatProvider(string id) : IChatCompletionProvider
    {
        public string ProviderId => id;
        public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        { await Task.CompletedTask; yield break; }
    }

    private sealed class StubRagIndex(IReadOnlyList<RagChunk> chunks) : IRagIndex
    {
        public Task<IReadOnlyList<RagChunk>> GetCandidatesAsync(string connectionId, CancellationToken cancellationToken) => Task.FromResult(chunks);
    }

    private sealed class StubDatabaseProvider(DatabaseCapabilities capabilities) : IDatabaseProvider
    {
        public string ProviderId => "mysql";
        public DatabaseCapabilities Capabilities => capabilities;
        public ISchemaIntrospector Schema => throw new NotSupportedException();
        public ISqlDialect Dialect => throw new NotSupportedException();
        public ISqlValidator Validator => throw new NotSupportedException();
        public IReadOnlyQueryExecutor Executor => throw new NotSupportedException();
    }
}

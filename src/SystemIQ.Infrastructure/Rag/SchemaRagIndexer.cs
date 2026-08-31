using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.AI;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Infrastructure.Rag;

public sealed record SchemaRagIndexOptions(
    string EmbeddingProfile, string EmbeddingModel, string EmbeddingVersion,
    string ContentVersion = "1", string GlossaryVersion = "none");

public sealed class SchemaRagIndexer(
    IEmbeddingProviderRegistry embeddings, IRagIndexStore store, SchemaRagIndexOptions options)
{
    public async Task<RagCompatibility> IndexAsync(
        string connectionId, SchemaSnapshot schema, CancellationToken cancellationToken,
        IReadOnlySet<string>? authorizedObjects = null)
    {
        var provider = embeddings.GetRequired(options.EmbeddingProfile);
        var definitions = BuildDefinitions(connectionId, schema, authorizedObjects);
        var vectors = await provider.EmbedAsync(definitions.Select(x => x.Text).ToArray(), cancellationToken).ConfigureAwait(false);
        if (vectors.Count != definitions.Count)
            throw new ProviderException(Domain.AI.ProviderErrorCategory.InvalidResponse,
                "Embedding provider returned an unexpected schema vector count.");

        var indexVersion = Hash($"{schema.SnapshotHash}|{options.GlossaryVersion}|{provider.ProviderId}|{options.EmbeddingModel}|{provider.Dimensions}|{options.ContentVersion}|{options.EmbeddingVersion}");
        var compatibility = new RagCompatibility(schema.SnapshotHash, options.GlossaryVersion, provider.ProviderId,
            options.EmbeddingModel, provider.Dimensions, options.ContentVersion, indexVersion);
        var chunks = definitions.Select((definition, index) =>
        {
            if (vectors[index].Dimensions != provider.Dimensions)
                throw new Domain.AI.EmbeddingDimensionException(provider.Dimensions, vectors[index].Dimensions);
            return new RagChunk(definition.Id, connectionId, definition.Type, definition.ObjectName,
                definition.Text, vectors[index], compatibility, definition.Metadata);
        }).ToArray();
        await store.PublishAsync(connectionId, chunks, compatibility, cancellationToken).ConfigureAwait(false);
        return compatibility;
    }

    public static IReadOnlyList<SchemaChunkDefinition> BuildDefinitions(
        string connectionId, SchemaSnapshot schema, IReadOnlySet<string>? authorizedObjects = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var result = new List<SchemaChunkDefinition>();
        foreach (var table in schema.Tables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var qualified = string.Join('.', new[] { table.Catalog, table.Schema, table.Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!IsAuthorized(table.Name, qualified, authorizedObjects)) continue;
            var primaryKeys = table.Columns.Where(x => x.IsPrimaryKey).Select(x => x.Name).ToArray();
            var tableText = $"Table {qualified}. Columns: {string.Join(", ", table.Columns.OrderBy(x => x.Ordinal).Select(ColumnDescription))}." +
                (primaryKeys.Length == 0 ? string.Empty : $" Primary key: {string.Join(", ", primaryKeys)}.");
            result.Add(Definition(connectionId, RagSourceType.Table, table.Name, tableText,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["table"] = table.Name, ["catalog"] = table.Catalog }));

            foreach (var column in table.Columns.OrderBy(x => x.Ordinal))
                result.Add(Definition(connectionId, RagSourceType.Column, $"{table.Name}.{column.Name}",
                    $"Column {qualified}.{column.Name}: type {column.NativeType}; nullable {column.IsNullable}; primary key {column.IsPrimaryKey}.",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["table"] = table.Name, ["column"] = column.Name }));
        }
        foreach (var relationship in schema.Relationships.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsAuthorized(relationship.FromTable, relationship.FromTable, authorizedObjects) ||
                !IsAuthorized(relationship.ToTable, relationship.ToTable, authorizedObjects)) continue;
            var text = $"Relationship {relationship.Name}: join {relationship.FromTable} ({string.Join(", ", relationship.FromColumns)}) to {relationship.ToTable} ({string.Join(", ", relationship.ToColumns)}).";
            result.Add(Definition(connectionId, RagSourceType.Relationship, relationship.Name, text,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["table"] = relationship.FromTable, ["fromTable"] = relationship.FromTable, ["toTable"] = relationship.ToTable
                }));
        }
        return result;
    }

    private static bool IsAuthorized(string name, string qualifiedName, IReadOnlySet<string>? authorizedObjects) =>
        authorizedObjects is null || authorizedObjects.Contains("*") ||
        authorizedObjects.Contains(name) || authorizedObjects.Contains(qualifiedName);

    private static string ColumnDescription(SchemaColumn column) =>
        $"{column.Name} {column.NativeType}{(column.IsNullable ? " nullable" : " required")}{(column.IsPrimaryKey ? " primary-key" : string.Empty)}";
    private static SchemaChunkDefinition Definition(string connectionId, RagSourceType type, string objectName,
        string text, IReadOnlyDictionary<string, string> metadata) =>
        new(Hash($"{connectionId}|{type}|{objectName}|{text}"), type, objectName, text, metadata);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record SchemaChunkDefinition(string Id, RagSourceType Type, string ObjectName, string Text,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class SchemaGroundingRagRetriever(
    IConnectionCatalog connections,
    IDatabaseProviderRegistry databases,
    SchemaRagIndexer indexer,
    VectorRagRetriever retriever) : IRagRetriever
{
    public async Task<RagRetrievalResult> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken)
    {
        var connection = await connections.FindAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected connection is unavailable.");
        var provider = databases.GetRequired(connection.ProviderId, DatabaseCapabilities.SchemaDiscovery);
        var schema = await provider.Schema.DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
        await indexer.IndexAsync(connection.Id, schema, cancellationToken, request.AuthorizedObjects).ConfigureAwait(false);
        return await retriever.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

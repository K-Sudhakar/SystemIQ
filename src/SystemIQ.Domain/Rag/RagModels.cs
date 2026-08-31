using SystemIQ.Domain.AI;

namespace SystemIQ.Domain.Rag;

public enum RagSourceType { Table, Column, Relationship, Glossary, Synonym }
public sealed record RagCompatibility(
    string SchemaHash, string GlossaryVersion, string EmbeddingProviderId, string EmbeddingModelId,
    int EmbeddingDimensions, string ContentVersion, string IndexVersion);
public sealed record RagChunk(
    string ChunkId, string ConnectionId, RagSourceType SourceType, string ObjectName, string Text,
    EmbeddingVector Vector, RagCompatibility Compatibility, IReadOnlyDictionary<string, string> Metadata);
public sealed record RagContextChunk(
    string ChunkId, string ConnectionId, RagSourceType SourceType, string ObjectName, string Text, double Score);
public sealed record RagRetrievalRequest(
    string Question, string ConnectionId, string EmbeddingProfile,
    IReadOnlySet<string> AuthorizedObjects, int TopK, int TokenBudget);
public sealed record RagRetrievalResult(IReadOnlyList<RagContextChunk> Chunks, bool IsDegraded, string IndexVersion);

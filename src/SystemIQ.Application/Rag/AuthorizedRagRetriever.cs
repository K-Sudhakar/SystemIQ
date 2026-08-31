using SystemIQ.Application.AI;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Application.Rag;

public sealed class AuthorizedRagRetriever(IEmbeddingProviderRegistry embeddings, IRagIndex index) : IRagRetriever
{
    public async Task<RagRetrievalResult> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        if (request.TopK <= 0) throw new ArgumentOutOfRangeException(nameof(request), "TopK must be positive.");
        if (request.TokenBudget <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Token budget must be positive.");

        var provider = embeddings.GetRequired(request.EmbeddingProfile);
        var vectors = await provider.EmbedAsync([request.Question], cancellationToken).ConfigureAwait(false);
        if (vectors.Count != 1)
            throw new ProviderException(Domain.AI.ProviderErrorCategory.InvalidResponse, "Embedding provider returned an unexpected vector count.");
        var query = vectors[0];
        if (query.Dimensions != provider.Dimensions)
            throw new Domain.AI.EmbeddingDimensionException(provider.Dimensions, query.Dimensions);

        var candidates = await index.GetCandidatesAsync(request.ConnectionId, cancellationToken).ConfigureAwait(false);
        var authorized = candidates
            .Where(c => StringComparer.Ordinal.Equals(c.ConnectionId, request.ConnectionId))
            .Where(c => IsAuthorized(c, request.AuthorizedObjects))
            .Where(c => c.Vector.Dimensions == provider.Dimensions)
            .Where(c => StringComparer.OrdinalIgnoreCase.Equals(c.Compatibility.EmbeddingProviderId, provider.ProviderId))
            .Select(c => new Ranked(c, ExactMatch(request.Question, c), Cosine(query, c.Vector)))
            .OrderByDescending(c => c.Exact)
            .ThenByDescending(c => c.Similarity)
            .ThenBy(c => c.Chunk.ChunkId, StringComparer.Ordinal)
            .Take(request.TopK);

        var remainingTokens = request.TokenBudget;
        var selected = new List<RagContextChunk>();
        foreach (var candidate in authorized)
        {
            var estimatedTokens = Math.Max(1, (candidate.Chunk.Text.Length + 3) / 4);
            if (estimatedTokens > remainingTokens) continue;
            remainingTokens -= estimatedTokens;
            selected.Add(new RagContextChunk(candidate.Chunk.ChunkId, candidate.Chunk.ConnectionId,
                candidate.Chunk.SourceType, candidate.Chunk.ObjectName, candidate.Chunk.Text,
                candidate.Exact ? 2d + candidate.Similarity : candidate.Similarity));
        }

        var version = selected.Count == 0 ? "none" : candidates.First(c => c.ChunkId == selected[0].ChunkId).Compatibility.IndexVersion;
        return new RagRetrievalResult(selected, false, version);
    }

    private static bool IsAuthorized(RagChunk chunk, IReadOnlySet<string> authorized) =>
        chunk.SourceType is RagSourceType.Glossary or RagSourceType.Synonym || authorized.Contains("*") || authorized.Contains(chunk.ObjectName) ||
        (chunk.Metadata.TryGetValue("table", out var table) && authorized.Contains(table));

    private static bool ExactMatch(string question, RagChunk chunk)
    {
        var terms = question.Split([' ', '\t', '\r', '\n', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
        return terms.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t, chunk.ObjectName)) ||
               terms.Any(t => chunk.SourceType is RagSourceType.Glossary or RagSourceType.Synonym &&
                              chunk.Text.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static double Cosine(Domain.AI.EmbeddingVector left, Domain.AI.EmbeddingVector right)
    {
        var a = left.Values.Span;
        var b = right.Values.Span;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private sealed record Ranked(RagChunk Chunk, bool Exact, double Similarity);
}

using SystemIQ.Application.AI;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Infrastructure.Rag;

public sealed class VectorRagRetriever(IRagIndex index, IEmbeddingProviderRegistry embeddings, bool allowLexicalDegradation = false) : IRagRetriever
{
    public async Task<RagRetrievalResult> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken)
    {
        if (request.TopK <= 0 || request.TokenBudget <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var candidates = (await index.GetCandidatesAsync(request.ConnectionId, cancellationToken))
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.ConnectionId, request.ConnectionId))
            .Where(x => IsAuthorized(x, request.AuthorizedObjects)).ToArray();
        if (candidates.Length == 0) return new RagRetrievalResult([], false, "empty");
        try
        {
            var provider = embeddings.GetRequired(request.EmbeddingProfile);
            if (candidates.Any(x => x.Compatibility.EmbeddingProviderId != provider.ProviderId || x.Compatibility.EmbeddingDimensions != provider.Dimensions))
                throw new InvalidOperationException("The RAG index is stale for the selected embedding provider or dimension.");
            var query = (await provider.EmbedAsync([request.Question], cancellationToken)).Single();
            var ranked = candidates.Select(chunk => (chunk, score: Cosine(query, chunk.Vector) + ExactBoost(request.Question, chunk)))
                .OrderByDescending(x => x.score).ThenBy(x => x.chunk.ChunkId, StringComparer.Ordinal).ToArray();
            return Build(ranked, request, false);
        }
        catch (ProviderException) when (allowLexicalDegradation)
        {
            var ranked = candidates.Select(chunk => (chunk, score: ExactBoost(request.Question, chunk)))
                .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenBy(x => x.chunk.ChunkId, StringComparer.Ordinal).ToArray();
            return Build(ranked, request, true);
        }
    }
    private static RagRetrievalResult Build(IEnumerable<(RagChunk chunk, double score)> ranked, RagRetrievalRequest request, bool degraded)
    {
        var result = new List<RagContextChunk>();
        var tokens = 0;
        string indexVersion = "unknown";
        foreach (var (chunk, score) in ranked.Take(request.TopK))
        {
            var estimated = Math.Max(1, (chunk.Text.Length + 3) / 4);
            if (tokens + estimated > request.TokenBudget) continue;
            tokens += estimated;
            indexVersion = chunk.Compatibility.IndexVersion;
            result.Add(new(chunk.ChunkId, chunk.ConnectionId, chunk.SourceType, chunk.ObjectName, chunk.Text, score));
        }
        return new RagRetrievalResult(result, degraded, indexVersion);
    }
    private static bool IsAuthorized(RagChunk chunk, IReadOnlySet<string> authorized)
    {
        if (authorized.Contains("*") || authorized.Contains(chunk.ObjectName)) return true;
        if (chunk.Metadata.TryGetValue("fromTable", out var from) && chunk.Metadata.TryGetValue("toTable", out var to))
            return authorized.Contains(from) && authorized.Contains(to);
        return chunk.Metadata.TryGetValue("table", out var table) && authorized.Contains(table);
    }
    private static double ExactBoost(string question, RagChunk chunk)
    {
        var terms = question.Split([' ', '\t', '\r', '\n', ',', '.', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Any(term => term.Length > 2 && (chunk.ObjectName.Equals(term, StringComparison.OrdinalIgnoreCase) || chunk.Text.Contains(term, StringComparison.OrdinalIgnoreCase))) ? 1d : 0d;
    }
    private static double Cosine(EmbeddingVector left, EmbeddingVector right)
    {
        if (left.Dimensions != right.Dimensions) throw new InvalidOperationException("The RAG index embedding dimensions are stale.");
        var a = left.Values.Span; var b = right.Values.Span;
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; magA += a[i] * a[i]; magB += b[i] * b[i]; }
        return magA == 0 || magB == 0 ? 0 : dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}

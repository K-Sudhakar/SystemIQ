using System.Collections.Concurrent;
using SystemIQ.Application.Rag;
using SystemIQ.Domain.Rag;

namespace SystemIQ.Infrastructure.Rag;

public sealed class InMemoryRagIndex : IRagIndex, IRagIndexStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<RagChunk>> _chunks = new(StringComparer.OrdinalIgnoreCase);
    public Task<IReadOnlyList<RagChunk>> GetCandidatesAsync(string connectionId, CancellationToken cancellationToken) => ReadChunksAsync(connectionId, cancellationToken);
    public Task<IReadOnlyList<RagChunk>> ReadChunksAsync(string connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_chunks.TryGetValue(connectionId, out var chunks) ? chunks : (IReadOnlyList<RagChunk>)[]);
    }
    public Task PublishAsync(string connectionId, IReadOnlyList<RagChunk> chunks, RagCompatibility compatibility, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (chunks.Any(x => !StringComparer.OrdinalIgnoreCase.Equals(x.ConnectionId, connectionId) || x.Compatibility != compatibility))
            throw new InvalidOperationException("Every RAG chunk must match its connection and published compatibility manifest.");
        _chunks[connectionId] = chunks.ToArray();
        return Task.CompletedTask;
    }
}

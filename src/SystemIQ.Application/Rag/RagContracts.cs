using SystemIQ.Domain.Rag;

namespace SystemIQ.Application.Rag;

public interface IRagIndex { Task<IReadOnlyList<RagChunk>> GetCandidatesAsync(string connectionId, CancellationToken cancellationToken); }
public interface IRagRetriever { Task<RagRetrievalResult> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken); }
public interface IRagIndexStore
{
    Task<IReadOnlyList<RagChunk>> ReadChunksAsync(string connectionId, CancellationToken cancellationToken);
    Task PublishAsync(string connectionId, IReadOnlyList<RagChunk> chunks, RagCompatibility compatibility, CancellationToken cancellationToken);
}

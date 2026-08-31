using SystemIQ.Domain.Queries;

namespace SystemIQ.Application.Queries;

public interface INaturalLanguageQueryOrchestrator
{
    Task<NaturalLanguageQueryResult> ExecuteAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken);
}
public interface IQueryHistorySink
{
    Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken);
}

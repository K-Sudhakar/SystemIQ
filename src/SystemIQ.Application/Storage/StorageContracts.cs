using SystemIQ.Domain.Storage;

namespace SystemIQ.Application.Storage;

public interface IObjectDocumentStore
{
    Task<Document<T>?> ReadAsync<T>(DocumentKey key, CancellationToken cancellationToken);
    Task<WriteResult> WriteAsync<T>(DocumentKey key, T value, WriteCondition condition, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(DocumentKey key, string? expectedVersion, CancellationToken cancellationToken);
    IAsyncEnumerable<Document<T>> ListAsync<T>(DocumentPrefix prefix, CancellationToken cancellationToken);
}
public interface IAccessDenialStore
{
    Task RecordAsync(string subject, DateTimeOffset occurredAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessDenialEvent>> GetWindowAsync(string subject, DateTimeOffset since, CancellationToken cancellationToken);
}
public interface ISecurityAuditSink
{
    Task WriteAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken);
}

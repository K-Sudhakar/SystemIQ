using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemSecurityAuditSink(IObjectDocumentStore documents) : ISecurityAuditSink
{
    public async Task WriteAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var material = $"{auditEvent.EventId}|{auditEvent.CorrelationId}|{auditEvent.EventType}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        await documents.WriteAsync(new DocumentKey("security-audit", [id]), auditEvent,
            new WriteCondition(WriteConditionKind.CreateOnly), cancellationToken).ConfigureAwait(false);
    }
}

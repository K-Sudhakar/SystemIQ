using System.Security.Cryptography;
using System.Text;
using SystemIQ.Application.Glossary;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Glossary;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class FileSystemGlossaryStore(IObjectDocumentStore documents) : IGlossaryStore
{
    public async Task<IReadOnlyList<GlossaryEntry>> ListAsync(
        string subject, string connectionId, int maxEntries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        var result = new List<GlossaryEntry>();
        await foreach (var document in documents.ListAsync<GlossaryDocument>(
            new DocumentPrefix("query-glossary", []), cancellationToken).ConfigureAwait(false))
        {
            if (document.Value.Subject.Equals(subject, StringComparison.Ordinal) &&
                document.Value.Entry.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase))
                result.Add(document.Value.Entry);
        }
        return result.OrderBy(entry => entry.Table, StringComparer.OrdinalIgnoreCase).Take(maxEntries).ToArray();
    }

    public async Task<GlossaryEntry> UpsertAsync(
        string subject, GlossaryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(entry);
        var id = HashIdentity(subject, entry.ConnectionId, entry.Table);
        await documents.WriteAsync(new DocumentKey("query-glossary", [id]),
            new GlossaryDocument(subject, entry), new WriteCondition(WriteConditionKind.Unconditional),
            cancellationToken).ConfigureAwait(false);
        return entry;
    }

    private static string HashIdentity(params string[] values)
    {
        var identity = string.Concat(values.Select(value => $"{value.Length}:{value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public sealed record GlossaryDocument(string Subject, GlossaryEntry Entry);
}

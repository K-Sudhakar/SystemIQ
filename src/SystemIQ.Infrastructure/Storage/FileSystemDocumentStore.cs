using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Infrastructure.Storage;

public sealed class DocumentConflictException(string message) : Exception(message);

public sealed class FileSystemDocumentStore : IObjectDocumentStore
{
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new(StringComparer.OrdinalIgnoreCase);
    public FileSystemDocumentStore(string root, JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task<Document<T>?> ReadAsync<T>(DocumentKey key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var value = JsonSerializer.Deserialize<T>(bytes, _jsonOptions) ?? throw new InvalidDataException("The stored document is null or malformed.");
        return new Document<T>(key, value, Hash(bytes));
    }

    public async Task<WriteResult> WriteAsync<T>(DocumentKey key, T value, WriteCondition condition, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        var gate = _writeLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            var tempPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            var existed = File.Exists(path);
            if (condition.Kind == WriteConditionKind.CreateOnly && existed) throw new DocumentConflictException("The document already exists.");
            if (condition.Kind == WriteConditionKind.MatchVersion && (!existed || !StringComparer.Ordinal.Equals(Hash(await File.ReadAllBytesAsync(path, cancellationToken)), condition.ExpectedVersion)))
                throw new DocumentConflictException("The document version no longer matches.");
            try
            {
                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(tempPath, path, condition.Kind != WriteConditionKind.CreateOnly);
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
            return new WriteResult(Hash(bytes), !existed);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(DocumentKey key, string? expectedVersion, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        var gate = _writeLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return false;
            if (expectedVersion is not null && !StringComparer.Ordinal.Equals(Hash(await File.ReadAllBytesAsync(path, cancellationToken)), expectedVersion))
                throw new DocumentConflictException("The document version no longer matches.");
            File.Delete(path);
            return true;
        }
        finally { gate.Release(); }
    }

    public async IAsyncEnumerable<Document<T>> ListAsync<T>(DocumentPrefix prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateSegment(prefix.Namespace);
        foreach (var segment in prefix.Segments) ValidateSegment(segment);
        var directory = Path.Combine(new[] { _root, prefix.Namespace }.Concat(prefix.Segments).ToArray());
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/')[..^5].Split('/');
            var document = await ReadAsync<T>(new DocumentKey(parts[0], parts[1..]), cancellationToken);
            if (document is not null) yield return document;
        }
    }

    private string Resolve(DocumentKey key)
    {
        ValidateSegment(key.Namespace);
        if (key.Segments.Count == 0) throw new ArgumentException("A document key needs at least one segment.", nameof(key));
        foreach (var segment in key.Segments) ValidateSegment(segment);
        var relative = Path.Combine(new[] { key.Namespace }.Concat(key.Segments).ToArray()) + ".json";
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The document key escapes the configured root.", nameof(key));
        return path;
    }
    private static void ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(['/', '\\', ':']) >= 0 || segment.Any(char.IsControl))
            throw new ArgumentException("The document key contains an unsafe segment.");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

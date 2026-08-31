namespace SystemIQ.Domain.Storage;

public sealed record DocumentKey
{
    public DocumentKey(string @namespace, IReadOnlyList<string> segments)
    {
        Namespace = ValidateNamespace(@namespace);
        Segments = ValidateSegments(segments, requireAny: true);
    }
    public string Namespace { get; }
    public IReadOnlyList<string> Segments { get; }
    internal static string ValidateNamespace(string value) => ValidateSegment(value, nameof(value));
    internal static IReadOnlyList<string> ValidateSegments(IReadOnlyList<string> values, bool requireAny)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (requireAny && values.Count == 0) throw new ArgumentException("At least one logical segment is required.", nameof(values));
        return values.Select(v => ValidateSegment(v, nameof(values))).ToArray();
    }
    private static string ValidateSegment(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || Path.IsPathRooted(value) ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':') || value.Any(char.IsControl) ||
            Uri.UnescapeDataString(value).Contains('/') || Uri.UnescapeDataString(value).Contains('\\'))
            throw new ArgumentException("Document path segments must be safe, non-empty logical names.", parameter);
        return value;
    }
}
public sealed record DocumentPrefix
{
    public DocumentPrefix(string @namespace, IReadOnlyList<string> segments)
    {
        Namespace = DocumentKey.ValidateNamespace(@namespace);
        Segments = DocumentKey.ValidateSegments(segments, requireAny: false);
    }
    public string Namespace { get; }
    public IReadOnlyList<string> Segments { get; }
}
public sealed record Document<T>(DocumentKey Key, T Value, string Version);
public enum WriteConditionKind { Unconditional, CreateOnly, MatchVersion }
public sealed record WriteCondition(WriteConditionKind Kind, string? ExpectedVersion = null);
public sealed record WriteResult(string Version, bool Created);
public sealed record AccessDenialEvent(string EventId, string Subject, DateTimeOffset OccurredAt);
public sealed record SecurityAuditEvent(
    string EventId, string EventType, string Subject, string? ConnectionId,
    string CorrelationId, string Reason, DateTimeOffset OccurredAt);

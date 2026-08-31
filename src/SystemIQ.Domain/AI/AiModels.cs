namespace SystemIQ.Domain.AI;

public enum ChatRole { System, User, Assistant }
public enum ChatPurpose { SqlGeneration, GroundedAnswer, General }
public enum ProviderErrorCategory { Authentication, RateLimited, Timeout, Unavailable, InvalidResponse, Policy }

public sealed record ChatMessage(ChatRole Role, string Content);
public sealed record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    ChatPurpose Purpose,
    string CorrelationId,
    string? ModelId = null,
    int? MaxOutputTokens = null);
public sealed record TokenUsage(int InputTokens, int OutputTokens);
public sealed record ChatCompletion(string Content, string ModelId, TokenUsage Usage, string FinishReason);
public sealed record ChatDelta(string Content, bool IsFinal = false);

public sealed class EmbeddingVector
{
    private readonly float[] _values;
    public EmbeddingVector(IEnumerable<float> values, int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (expectedDimensions <= 0) throw new ArgumentOutOfRangeException(nameof(expectedDimensions));
        _values = values.ToArray();
        if (_values.Length != expectedDimensions)
            throw new EmbeddingDimensionException(expectedDimensions, _values.Length);
        if (_values.Any(v => !float.IsFinite(v)))
            throw new ArgumentException("Embedding values must be finite.", nameof(values));
    }
    public int Dimensions => _values.Length;
    public ReadOnlyMemory<float> Values => _values;
}

public sealed class EmbeddingDimensionException(int expectedDimensions, int actualDimensions)
    : Exception($"Embedding dimension mismatch: expected {expectedDimensions}, received {actualDimensions}.")
{
    public int ExpectedDimensions { get; } = expectedDimensions;
    public int ActualDimensions { get; } = actualDimensions;
}

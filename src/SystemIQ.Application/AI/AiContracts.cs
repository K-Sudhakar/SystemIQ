using SystemIQ.Domain.AI;

namespace SystemIQ.Application.AI;

public interface IChatCompletionProvider
{
    string ProviderId { get; }
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}
public interface IEmbeddingProvider
{
    string ProviderId { get; }
    int Dimensions { get; }
    Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> input, CancellationToken cancellationToken);
}
public interface IChatProviderRegistry { IChatCompletionProvider GetRequired(string profile); }
public interface IEmbeddingProviderRegistry { IEmbeddingProvider GetRequired(string profile); }

public sealed class ProviderException(ProviderErrorCategory category, string message, bool isRetryable = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ProviderErrorCategory Category { get; } = category;
    public bool IsRetryable { get; } = isRetryable;
}
public sealed class UnknownProviderException(string category, string id) : KeyNotFoundException($"Unknown {category} provider/profile '{id}'.");
public sealed class DuplicateProviderException(string category, string id) : InvalidOperationException($"Duplicate {category} provider/profile '{id}'.");

public sealed class ChatProviderRegistry : IChatProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IChatCompletionProvider> _providers;
    public ChatProviderRegistry(IEnumerable<IChatCompletionProvider> providers) => _providers = Build(providers);
    public IChatCompletionProvider GetRequired(string profile) => _providers.TryGetValue(profile, out var provider) ? provider : throw new UnknownProviderException("chat", profile);
    private static IReadOnlyDictionary<string, IChatCompletionProvider> Build(IEnumerable<IChatCompletionProvider> providers)
    {
        var result = new Dictionary<string, IChatCompletionProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
            if (!result.TryAdd(provider.ProviderId, provider)) throw new DuplicateProviderException("chat", provider.ProviderId);
        return result;
    }
}
public sealed class EmbeddingProviderRegistry : IEmbeddingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IEmbeddingProvider> _providers;
    public EmbeddingProviderRegistry(IEnumerable<IEmbeddingProvider> providers) => _providers = Build(providers);
    public IEmbeddingProvider GetRequired(string profile) => _providers.TryGetValue(profile, out var provider) ? provider : throw new UnknownProviderException("embedding", profile);
    private static IReadOnlyDictionary<string, IEmbeddingProvider> Build(IEnumerable<IEmbeddingProvider> providers)
    {
        var result = new Dictionary<string, IEmbeddingProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
            if (!result.TryAdd(provider.ProviderId, provider)) throw new DuplicateProviderException("embedding", provider.ProviderId);
        return result;
    }
}

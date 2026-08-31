using SystemIQ.Application.AI;
using SystemIQ.Application.Secrets;
using SystemIQ.Domain.AI;
using SystemIQ.Domain.Secrets;

namespace SystemIQ.Infrastructure.AI;

public sealed record OpenAiProviderConfiguration(
    string Profile, Uri BaseUrl, string Model, TimeSpan Timeout,
    string? CredentialReference = null, int? Dimensions = null);

public sealed class SecretResolvingOpenAiChatProvider(
    HttpClient client, ISecretResolver secrets, OpenAiProviderConfiguration configuration) : IChatCompletionProvider
{
    public string ProviderId => configuration.Profile;
    public Uri BaseUrl => configuration.BaseUrl;
    public string Model => configuration.Model;

    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken) =>
        await WithProviderAsync(provider => provider.CompleteAsync(request, cancellationToken), cancellationToken).ConfigureAwait(false);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completion = await CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        yield return new ChatDelta(completion.Content, true);
    }

    private async Task<T> WithProviderAsync<T>(Func<OpenAiCompatibleChatProvider, Task<T>> action, CancellationToken cancellationToken)
    {
        using var secret = await ResolveSecretAsync(secrets, configuration.CredentialReference, cancellationToken).ConfigureAwait(false);
        var apiKey = secret is null ? null : new string(secret.Reveal().Span);
        try
        {
            return await action(new OpenAiCompatibleChatProvider(client,
                new OpenAiCompatibleOptions(configuration.Profile, configuration.BaseUrl, configuration.Model,
                    configuration.Timeout, apiKey))).ConfigureAwait(false);
        }
        finally { if (apiKey is not null) apiKey = string.Empty; }
    }

    internal static async Task<SecretValue?> ResolveSecretAsync(ISecretResolver resolver, string? reference, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(reference) ? null :
            await resolver.ResolveAsync(new SecretReference(reference), cancellationToken).ConfigureAwait(false);
}

public sealed class SecretResolvingOpenAiEmbeddingProvider(
    HttpClient client, ISecretResolver secrets, OpenAiProviderConfiguration configuration) : IEmbeddingProvider
{
    public string ProviderId => configuration.Profile;
    public int Dimensions => configuration.Dimensions ?? throw new InvalidOperationException("Embedding dimensions are not configured.");
    public Uri BaseUrl => configuration.BaseUrl;
    public string Model => configuration.Model;

    public async Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> input, CancellationToken cancellationToken)
    {
        using var secret = await SecretResolvingOpenAiChatProvider.ResolveSecretAsync(
            secrets, configuration.CredentialReference, cancellationToken).ConfigureAwait(false);
        var apiKey = secret is null ? null : new string(secret.Reveal().Span);
        try
        {
            var provider = new OpenAiCompatibleEmbeddingProvider(client,
                new OpenAiCompatibleOptions(configuration.Profile, configuration.BaseUrl, configuration.Model,
                    configuration.Timeout, apiKey, Dimensions));
            return await provider.EmbedAsync(input, cancellationToken).ConfigureAwait(false);
        }
        finally { if (apiKey is not null) apiKey = string.Empty; }
    }
}

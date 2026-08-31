using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemIQ.Application.AI;
using SystemIQ.Domain.AI;

namespace SystemIQ.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _client;
    private readonly OpenAiCompatibleOptions _options;
    public OpenAiCompatibleEmbeddingProvider(HttpClient client, OpenAiCompatibleOptions options)
    {
        _client = client;
        _options = options;
        if (options.Dimensions is null or <= 0) throw new ArgumentException("Embedding dimensions must be configured.", nameof(options));
        if (!options.BaseUrl.IsAbsoluteUri || options.Timeout <= TimeSpan.Zero) throw new ArgumentException("Embedding URL and timeout must be valid.", nameof(options));
    }
    public string ProviderId => _options.ProviderId;
    public int Dimensions => _options.Dimensions!.Value;

    public async Task<IReadOnlyList<EmbeddingVector>> EmbedAsync(IReadOnlyList<string> input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Count == 0) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, "v1/embeddings"));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        message.Content = JsonContent.Create(new EmbeddingPayload(_options.Model, input));
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await OpenAiCompatibleChatProvider.EnsureSuccessAsync(response, timeout.Token);
            var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: timeout.Token);
            if (body?.Data is null || body.Data.Count != input.Count) throw new ProviderException(ProviderErrorCategory.InvalidResponse, "Embedding provider returned an unexpected vector count.");
            return body.Data.OrderBy(x => x.Index).Select(x => new EmbeddingVector(x.Embedding, Dimensions)).ToArray();
        }
        catch (EmbeddingDimensionException) { throw; }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderException(ProviderErrorCategory.Timeout, "Embedding provider timed out.", true, exception); }
        catch (HttpRequestException exception)
        { throw new ProviderException(ProviderErrorCategory.Unavailable, "Embedding provider is unavailable.", true, exception); }
        catch (JsonException exception)
        { throw new ProviderException(ProviderErrorCategory.InvalidResponse, "Embedding provider returned invalid JSON.", false, exception); }
    }
    private sealed record EmbeddingPayload(string Model, IReadOnlyList<string> Input);
    private sealed record EmbeddingResponse(IReadOnlyList<EmbeddingItem>? Data);
    private sealed record EmbeddingItem(int Index, IReadOnlyList<float> Embedding);
}

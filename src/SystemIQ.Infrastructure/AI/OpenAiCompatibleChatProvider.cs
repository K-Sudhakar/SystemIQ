using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemIQ.Application.AI;
using SystemIQ.Domain.AI;

namespace SystemIQ.Infrastructure.AI;

public sealed class OpenAiCompatibleChatProvider : IChatCompletionProvider
{
    private readonly HttpClient _client;
    private readonly OpenAiCompatibleOptions _options;
    public OpenAiCompatibleChatProvider(HttpClient client, OpenAiCompatibleOptions options)
    {
        _client = client;
        _options = options;
        Validate(options);
    }
    public string ProviderId => _options.ProviderId;

    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, "v1/chat/completions"));
        AddAuthorization(message);
        message.Content = JsonContent.Create(new ChatPayload(request.ModelId ?? _options.Model, request.Messages.Select(x => new ChatMessagePayload(x.Role.ToString().ToLowerInvariant(), x.Content)).ToArray(), false, request.MaxOutputTokens));
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: timeout.Token);
            var choice = body?.Choices?.FirstOrDefault();
            if (choice?.Message?.Content is null) throw new ProviderException(ProviderErrorCategory.InvalidResponse, "Chat provider returned no completion.");
            return new ChatCompletion(choice.Message.Content, body!.Model ?? request.ModelId ?? _options.Model,
                new TokenUsage(body.Usage?.PromptTokens ?? 0, body.Usage?.CompletionTokens ?? 0), choice.FinishReason ?? "unknown");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderException(ProviderErrorCategory.Timeout, "Chat provider timed out.", true, exception); }
        catch (HttpRequestException exception)
        { throw new ProviderException(ProviderErrorCategory.Unavailable, "Chat provider is unavailable.", true, exception); }
        catch (JsonException exception)
        { throw new ProviderException(ProviderErrorCategory.InvalidResponse, "Chat provider returned invalid JSON.", false, exception); }
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completion = await CompleteAsync(request, cancellationToken);
        yield return new ChatDelta(completion.Content, true);
    }

    private void AddAuthorization(HttpRequestMessage message)
    { if (!string.IsNullOrWhiteSpace(_options.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey); }
    private static void Validate(OpenAiCompatibleOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        if (!options.BaseUrl.IsAbsoluteUri) throw new ArgumentException("AI BaseUrl must be absolute.");
        if (options.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
    }
    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsStringAsync(cancellationToken); // drain, never include provider body/secrets in errors
        var category = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorCategory.Authentication,
            HttpStatusCode.TooManyRequests => ProviderErrorCategory.RateLimited,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ProviderErrorCategory.Timeout,
            _ => ProviderErrorCategory.Unavailable
        };
        throw new ProviderException(category, $"AI provider request failed with HTTP {(int)response.StatusCode}.", category is ProviderErrorCategory.RateLimited or ProviderErrorCategory.Timeout or ProviderErrorCategory.Unavailable);
    }
    private sealed record ChatPayload(string Model, IReadOnlyList<ChatMessagePayload> Messages, bool Stream, [property: JsonPropertyName("max_tokens")] int? MaxTokens);
    private sealed record ChatMessagePayload(string Role, string Content);
    private sealed record ChatResponse(string? Model, IReadOnlyList<Choice>? Choices, Usage? Usage);
    private sealed record Choice(Message? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);
    private sealed record Message(string? Content);
    private sealed record Usage([property: JsonPropertyName("prompt_tokens")] int PromptTokens, [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}

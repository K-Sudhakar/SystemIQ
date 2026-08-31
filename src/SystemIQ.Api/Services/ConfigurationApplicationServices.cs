using Microsoft.Extensions.Options;
using SystemIQ.Api.Options;
using SystemIQ.Application.Databases;
using SystemIQ.Application.AI;
using SystemIQ.Application.Secrets;
using SystemIQ.Application.Queries;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Queries;
using SystemIQ.Infrastructure.AI;

namespace SystemIQ.Api.Services;

internal sealed class ConfigurationConnectionCatalog(
    IOptions<ConnectionCatalogOptions> options,
    IDatabaseProviderRegistry providers,
    ISecretResolver secrets) : IConnectionCatalog
{
    public async Task<DatabaseConnection?> FindAsync(string connectionId, CancellationToken cancellationToken)
    {
        var item = options.Value.Connections.SingleOrDefault(x => x.Id.Equals(connectionId, StringComparison.OrdinalIgnoreCase));
        return item is not null && await IsAvailableAsync(item, cancellationToken) ? ToDomain(item) : null;
    }

    public async Task<IReadOnlyList<ConnectionSummary>> ListPermittedAsync(ConnectionAccessPolicy policy, CancellationToken cancellationToken)
    {
        var result = new List<ConnectionSummary>();
        foreach (var item in options.Value.Connections.Where(x => policy.CanAccessConnection(x.Id)))
        {
            if (!await IsAvailableAsync(item, cancellationToken)) continue;
            result.Add(new ConnectionSummary(item.Id, item.DisplayName, item.Provider,
                providers.GetRequired(item.Provider).Capabilities));
        }
        return result;
    }

    private async Task<bool> IsAvailableAsync(ConnectionOptions item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.CredentialRef)) return false;
        try
        {
            _ = providers.GetRequired(item.Provider, DatabaseCapabilities.ConnectionValidation);
            using var secret = await secrets.ResolveAsync(new SystemIQ.Domain.Secrets.SecretReference(item.CredentialRef), cancellationToken);
            return secret.Reveal().Length > 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return false; }
    }

    private static DatabaseConnection ToDomain(ConnectionOptions item) =>
        new(item.Id, item.DisplayName, item.Provider, item.CredentialRef ?? string.Empty, item.Options);
}

internal sealed class ConfigurationConnectionAccessPolicyProvider(
    IOptions<ConnectionCatalogOptions> catalog,
    IOptionsMonitor<AccessPolicyOptions> policies) : IConnectionAccessPolicyProvider
{
    public Task<ConnectionAccessPolicy> GetAsync(string subject, CancellationToken cancellationToken)
    {
        var configured = policies.CurrentValue.Subjects.SingleOrDefault(x => x.Subject.Equals(subject, StringComparison.Ordinal));
        if (configured is null)
            return Task.FromResult(new ConnectionAccessPolicy(subject,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)));

        var allConnectionIds = catalog.Value.Connections.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowAllConnections = configured.Connections.Any(x => x.Id == "*");
        var ids = (allowAllConnections ? allConnectionIds : configured.Connections.Select(x => x.Id).Where(allConnectionIds.Contains))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wildcard = configured.Connections.FirstOrDefault(x => x.Id == "*");
        var objects = ids.ToDictionary(id => id, id => (IReadOnlySet<string>)new HashSet<string>(
            configured.Connections.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Objects
            ?? wildcard?.Objects ?? [], StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(new ConnectionAccessPolicy(subject, ids, objects));
    }
}

internal sealed class UnavailableNaturalLanguageQueryOrchestrator : INaturalLanguageQueryOrchestrator
{
    public Task<NaturalLanguageQueryResult> ExecuteAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(NaturalLanguageQueryResult.Failed(new QueryFailure(QueryFailureCode.ProviderUnavailable,
            "The NL-to-SQL application pipeline is not registered.", true)));
}

internal sealed class ConfigurationChatProviderRegistry(
    IOptions<AiOptions> options, IHttpClientFactory clients, ISecretResolver secrets) : IChatProviderRegistry
{
    public IChatCompletionProvider GetRequired(string profile)
    {
        var configured = options.Value.Chat;
        if (!configured.Provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
            !configured.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase) || configured.BaseUrl is null)
            throw new UnknownProviderException("chat", profile);
        return new SecretResolvingOpenAiChatProvider(clients.CreateClient("SystemIQ.Chat"), secrets,
            new OpenAiProviderConfiguration(configured.Profile, configured.BaseUrl, configured.Model,
                TimeSpan.FromSeconds(configured.TimeoutSeconds), configured.CredentialRef));
    }
}

internal sealed class ConfigurationEmbeddingProviderRegistry(
    IOptions<AiOptions> options, IHttpClientFactory clients, ISecretResolver secrets) : IEmbeddingProviderRegistry
{
    public IEmbeddingProvider GetRequired(string profile)
    {
        var configured = options.Value.Embeddings;
        if (!configured.Provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
            !configured.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase) || configured.BaseUrl is null)
            throw new UnknownProviderException("embedding", profile);
        return new SecretResolvingOpenAiEmbeddingProvider(clients.CreateClient("SystemIQ.Embeddings"), secrets,
            new OpenAiProviderConfiguration(configured.Profile, configured.BaseUrl, configured.Model,
                TimeSpan.FromSeconds(configured.TimeoutSeconds), configured.CredentialRef, configured.Dimensions));
    }
}

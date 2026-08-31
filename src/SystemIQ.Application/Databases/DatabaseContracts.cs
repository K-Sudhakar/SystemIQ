using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Rag;
using SystemIQ.Application.AI;

namespace SystemIQ.Application.Databases;

public interface IDatabaseProvider
{
    string ProviderId { get; }
    DatabaseCapabilities Capabilities { get; }
    ISchemaIntrospector Schema { get; }
    ISqlDialect Dialect { get; }
    ISqlValidator Validator { get; }
    IReadOnlyQueryExecutor Executor { get; }
}
public interface ISchemaIntrospector { Task<SchemaSnapshot> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken); }
public interface ISqlDialect
{
    string IdentifierQuote { get; }
    string BuildSqlGenerationGuidance(SchemaSnapshot schema, IReadOnlyList<RagContextChunk> context);
}
public interface ISqlValidator { Task<SqlValidationResult> ValidateAsync(string sql, SqlValidationContext context, CancellationToken cancellationToken); }
public interface ISqlAstParser { Task<ParsedSqlStatement> ParseAsync(string sql, string dialect, CancellationToken cancellationToken); }
public interface IReadOnlyQueryExecutor { Task<QueryRows> ExecuteAsync(ValidatedSql query, DatabaseConnection connection, QueryExecutionLimits limits, CancellationToken cancellationToken); }
public interface IDatabaseProviderRegistry { IDatabaseProvider GetRequired(string providerId, DatabaseCapabilities requiredCapabilities = DatabaseCapabilities.None); }
public interface IConnectionCatalog
{
    Task<DatabaseConnection?> FindAsync(string connectionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConnectionSummary>> ListPermittedAsync(ConnectionAccessPolicy policy, CancellationToken cancellationToken);
}
public interface IConnectionAccessPolicyProvider
{
    Task<ConnectionAccessPolicy> GetAsync(string subject, CancellationToken cancellationToken);
}

public sealed class MissingDatabaseCapabilityException(string providerId, DatabaseCapabilities missingCapabilities)
    : InvalidOperationException($"Database provider '{providerId}' is missing required capabilities: {missingCapabilities}.")
{ public DatabaseCapabilities MissingCapabilities { get; } = missingCapabilities; }

public sealed class DatabaseProviderRegistry : IDatabaseProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IDatabaseProvider> _providers;
    public DatabaseProviderRegistry(IEnumerable<IDatabaseProvider> providers)
    {
        var result = new Dictionary<string, IDatabaseProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
            if (!result.TryAdd(provider.ProviderId, provider)) throw new DuplicateProviderException("database", provider.ProviderId);
        _providers = result;
    }
    public IDatabaseProvider GetRequired(string providerId, DatabaseCapabilities requiredCapabilities = DatabaseCapabilities.None)
    {
        if (!_providers.TryGetValue(providerId, out var provider)) throw new UnknownProviderException("database", providerId);
        var missing = requiredCapabilities & ~provider.Capabilities;
        return missing == DatabaseCapabilities.None ? provider : throw new MissingDatabaseCapabilityException(providerId, missing);
    }
}

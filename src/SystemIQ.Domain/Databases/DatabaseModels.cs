namespace SystemIQ.Domain.Databases;

[Flags]
public enum DatabaseCapabilities
{
    None = 0,
    ConnectionValidation = 1,
    SchemaDiscovery = 2,
    DialectRendering = 4,
    SqlValidation = 8,
    ReadOnlyExecution = 16,
    All = ConnectionValidation | SchemaDiscovery | DialectRendering | SqlValidation | ReadOnlyExecution
}

public sealed record DatabaseConnection(
    string Id,
    string DisplayName,
    string ProviderId,
    string CredentialReference,
    IReadOnlyDictionary<string, string> Options);
public sealed record ConnectionSummary(string Id, string DisplayName, string ProviderId, DatabaseCapabilities Capabilities);
public sealed record ConnectionAccessPolicy(
    string Subject, IReadOnlySet<string> ConnectionIds,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AuthorizedObjects)
{
    public bool CanAccessConnection(string connectionId) => ConnectionIds.Contains(connectionId);
    public IReadOnlySet<string> ObjectsFor(string connectionId) =>
        AuthorizedObjects.TryGetValue(connectionId, out var objects) ? objects : new HashSet<string>();
}
public sealed record DatabaseIdentifier(string Value);
public sealed record SchemaColumn(string Name, string NativeType, bool IsNullable, int Ordinal, bool IsPrimaryKey = false);
public sealed record SchemaTable(string Catalog, string? Schema, string Name, IReadOnlyList<SchemaColumn> Columns);
public sealed record SchemaRelationship(
    string Name, string FromTable, IReadOnlyList<string> FromColumns, string ToTable, IReadOnlyList<string> ToColumns);
public sealed record SchemaSnapshot(
    string ProviderId, string ProviderVersion, string Catalog, string? Schema,
    IReadOnlyList<SchemaTable> Tables, IReadOnlyList<SchemaRelationship> Relationships,
    string SnapshotHash, DateTimeOffset CapturedAt);

public sealed record SqlObjectReference(string? Catalog, string? Schema, string Name);
public enum SqlStatementKind { Query, Mutation, Definition, Procedure, Unknown }
public sealed record ParsedSqlStatement(
    SqlStatementKind Kind, IReadOnlyList<SqlObjectReference> ReferencedObjects,
    bool HasLockingClause, bool HasOutputClause, bool HasMultipleStatements);
public sealed record SqlValidationContext(
    string Dialect, IReadOnlySet<string> AuthorizedObjects, int MaxRows,
    bool RequireBoundedQuery = true, string? AllowedCatalog = null);
public sealed record ValidatedSql(string Text, IReadOnlyList<SqlObjectReference> ReferencedObjects);
public sealed record SqlValidationResult(bool IsAllowed, ValidatedSql? Query, string? RejectionReason)
{
    public static SqlValidationResult Allow(string sql, IReadOnlyList<SqlObjectReference> references) =>
        new(true, new ValidatedSql(sql, references), null);
    public static SqlValidationResult Reject(string reason) => new(false, null, reason);
}
public sealed record QueryExecutionLimits(int MaxRows, long MaxBytes, TimeSpan Timeout)
{
    public static QueryExecutionLimits Default { get; } = new(500, 5 * 1024 * 1024, TimeSpan.FromSeconds(30));
}
public sealed record QueryRows(
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool WasTruncated, long SerializedBytes);

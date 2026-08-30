using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using SystemIQ.Functions.Models;

namespace SystemIQ.Functions.Services;

public sealed class SqlSafetyValidator
{
    private static readonly Regex MutatingKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|UPSERT|DROP|ALTER|CREATE|TRUNCATE|EXEC(?:UTE)?|GRANT|REVOKE|DENY|DBCC|BULK|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReadPrefix = new(
        @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public void EnsureReadOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || !ReadPrefix.IsMatch(sql) || MutatingKeyword.IsMatch(sql) ||
            sql.Contains("--", StringComparison.Ordinal) || sql.Contains("/*", StringComparison.Ordinal) ||
            sql.TrimEnd().TrimEnd(';').Contains(';'))
        {
            throw new UnauthorizedAccessException("Only a single read-only SELECT statement is permitted.");
        }
    }

    public static IReadOnlySet<string> ReferencedIdentifiers(string sql)
    {
        var matches = Regex.Matches(sql,
            @"\b(?:FROM|JOIN)\s+(?:(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*)?\[?(?<table>[A-Za-z_][A-Za-z0-9_]*)\]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return matches.Select(m => m.Groups["table"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> ReferencedColumns(string sql) =>
        Regex.Matches(sql, @"(?:\b|\])(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?(?<column>[A-Za-z_][A-Za-z0-9_]*)\]?(?=\s*(?:,|FROM|WHERE|JOIN|ON|=|<|>|\)|$))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(m => m.Groups["column"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool ReferencesIdentifier(string sql, string identifier) =>
        !string.IsNullOrWhiteSpace(identifier) &&
        Regex.IsMatch(
            sql,
            $@"(?<![A-Za-z0-9_])\[?{Regex.Escape(identifier)}\]?(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool SelectsWildcard(string sql)
    {
        var withoutCount = Regex.Replace(
            sql,
            @"\bCOUNT(?:_BIG)?\s*\(\s*\*\s*\)",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Be deliberately conservative: any remaining asterisk may expand a
        // row projection. This can reject multiplication expressions when a
        // denied-column policy exists, preferring fail-closed behavior.
        return withoutCount.Contains('*', StringComparison.Ordinal);
    }
}

public sealed class AuditLogService
{
    private readonly StorageClientFactory _clients;
    private readonly TimeProvider _clock;

    public AuditLogService(StorageClientFactory clients, TimeProvider clock)
    {
        _clients = clients;
        _clock = clock;
    }

    public async Task LogDeniedAccessAsync(
        string userObjectId,
        string questionOrSql,
        string connectionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var entry = new AuditLogEntry(userObjectId, questionOrSql, connectionId, reason, _clock.GetUtcNow());
        BlobContainerClient container;
        try
        {
            container = _clients.BlobContainer("AUDIT_LOG_BLOB_CONTAINER_URI", "audit-log");
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException("AUDIT_LOG_BLOB_CONTAINER_URI is required; audit logging fails closed.", ex);
        }
        var name = $"audit-log/{Safe(userObjectId)}/{entry.Timestamp:yyyyMMddTHHmmssfffffffZ}_{Guid.NewGuid():N}.json";
        var data = BinaryData.FromObjectAsJson(entry, JsonOptions.Default);
        await container.GetBlobClient(name).UploadAsync(data, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        }, cancellationToken);
    }

    private static string Safe(string value) => Regex.Replace(value, @"[^A-Za-z0-9_.-]", "_");
}

public sealed record RateLimitStatus(bool IsLimited, DateTimeOffset? RetryAfter, int Count);

public sealed class AccessDenialRateLimiter
{
    private readonly StorageClientFactory _clients;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccessDenialRateLimiter> _logger;

    public AccessDenialRateLimiter(StorageClientFactory clients, TimeProvider clock, ILogger<AccessDenialRateLimiter> logger)
    {
        _clients = clients;
        _clock = clock;
        _logger = logger;
    }

    public int Threshold => int.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_DENIAL_COUNT"), out var n) && n > 0 ? n : 5;
    public TimeSpan Window => TimeSpan.FromMinutes(
        int.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_WINDOW_MINUTES"), out var n) && n > 0 ? n : 10);

    public async Task RecordDenialAsync(string userObjectId, CancellationToken cancellationToken)
    {
        if (IsLocalDemo)
        {
            return;
        }
        var table = Client();
        var now = _clock.GetUtcNow();
        var entity = new TableEntity(Hash(userObjectId), $"{now.UtcDateTime.Ticks:D19}-{Guid.NewGuid():N}")
        {
            ["DeniedAt"] = now
        };
        await table.AddEntityAsync(entity, cancellationToken);
    }

    public async Task<RateLimitStatus> GetStatusAsync(string userObjectId, CancellationToken cancellationToken)
    {
        if (IsLocalDemo)
        {
            return new(false, null, 0);
        }
        var cutoff = _clock.GetUtcNow() - Window;
        var rows = new List<DateTimeOffset>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {Hash(userObjectId)} and DeniedAt ge {cutoff}");
        await foreach (var item in Client().QueryAsync<TableEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            if (item.GetDateTimeOffset("DeniedAt") is { } deniedAt)
            {
                rows.Add(deniedAt);
            }
        }

        if (rows.Count < Threshold)
        {
            return new(false, null, rows.Count);
        }

        var retry = rows.Order().Skip(Math.Max(0, rows.Count - Threshold)).First() + Window;
        _logger.LogWarning("Access denial rate limit triggered for user hash {UserHash}", Hash(userObjectId));
        return new(true, retry, rows.Count);
    }

    private TableClient Client()
    {
        return _clients.Table(
            "RATE_LIMIT_TABLE_ENDPOINT",
            Environment.GetEnvironmentVariable("RATE_LIMIT_TABLE_NAME") ?? "AccessDenials");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static bool IsLocalDemo =>
        Environment.GetEnvironmentVariable("LOCAL_DEMO_MODE")
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class AccessPolicyService
{
    private readonly SqlSafetyValidator _sql;
    private readonly AuditLogService _audit;
    private readonly AccessDenialRateLimiter _limiter;
    private readonly ILogger<AccessPolicyService> _logger;

    public AccessPolicyService(SqlSafetyValidator sql, AuditLogService audit, AccessDenialRateLimiter limiter, ILogger<AccessPolicyService> logger)
    {
        _sql = sql;
        _audit = audit;
        _limiter = limiter;
        _logger = logger;
    }

    public AccessPolicy GetPolicy(string userObjectId, IEnumerable<string> roles)
    {
        var json = Environment.GetEnvironmentVariable("RBAC_POLICY_JSON") ?? "{}";
        using var document = JsonDocument.Parse(json);
        var connections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var columns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var keys = roles.Append(userObjectId).Append("*");
        foreach (var key in keys)
        {
            if (!document.RootElement.TryGetProperty(key, out var rule))
            {
                continue;
            }
            ReadArray(rule, "connections", connections);
            ReadMap(rule, "allowedTables", tables);
            ReadMap(rule, "deniedColumns", columns);
        }
        return new(connections, tables.ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Value),
            columns.ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Value));
    }

    public async Task EnsureGeneratedSqlAllowedAsync(
        string userObjectId,
        AccessPolicy policy,
        string connectionId,
        string question,
        string sql,
        CancellationToken cancellationToken)
    {
        string? denial = null;
        try
        {
            _sql.EnsureReadOnly(sql);
        }
        catch (UnauthorizedAccessException ex)
        {
            denial = ex.Message;
        }

        if (!policy.Connections.Contains(connectionId))
        {
            denial = "Connection is not permitted.";
        }
        else if (policy.AllowedTables.TryGetValue(connectionId, out var allowed) &&
                 SqlSafetyValidator.ReferencedIdentifiers(sql).Any(table => !allowed.Contains(table)))
        {
            denial = "Query references a table outside the access policy.";
        }
        else if (policy.DeniedColumns.TryGetValue(connectionId, out var denied) &&
                 denied.Count > 0 &&
                 (SqlSafetyValidator.SelectsWildcard(sql) ||
                  denied.Any(column => SqlSafetyValidator.ReferencesIdentifier(sql, column))))
        {
            denial = "Query references a column outside the access policy.";
        }

        if (denial is null)
        {
            return;
        }

        await RecordDenialAsync(
            userObjectId,
            $"{question}\nSQL: {sql}",
            connectionId,
            denial,
            cancellationToken);
        throw new UnauthorizedAccessException("The requested data is not available under your access policy.");
    }

    public async Task RecordDenialAsync(
        string userObjectId,
        string questionOrSql,
        string connectionId,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.LogDeniedAccessAsync(
                userObjectId,
                questionOrSql,
                connectionId,
                reason,
                cancellationToken);
            await _limiter.RecordDenialAsync(userObjectId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist mandatory denial controls.");
            throw new InvalidOperationException(
                "A security audit system error occurred. Try again shortly.",
                ex);
        }
    }

    private static void ReadArray(JsonElement rule, string name, HashSet<string> destination)
    {
        if (rule.TryGetProperty(name, out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.GetString() is { } value) destination.Add(value);
            }
        }
    }

    private static void ReadMap(JsonElement rule, string name, IDictionary<string, HashSet<string>> destination)
    {
        if (!rule.TryGetProperty(name, out var map)) return;
        foreach (var property in map.EnumerateObject())
        {
            if (!destination.TryGetValue(property.Name, out var set))
            {
                destination[property.Name] = set = new(StringComparer.OrdinalIgnoreCase);
            }
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.GetString() is { } value) set.Add(value);
            }
        }
    }
}

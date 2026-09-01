using System.ComponentModel.DataAnnotations;

namespace SystemIQ.Api.Options;

public sealed class SystemIqOptions
{
    public const string SectionName = "SystemIQ";
    public string DeploymentMode { get; set; } = "SingleProcessDevelopment";
}

public sealed class AiOptions
{
    public const string SectionName = "AI";
    public AiProviderOptions Chat { get; set; } = new();
    public EmbeddingProviderOptions Embeddings { get; set; } = new();
}

public class AiProviderOptions
{
    public string Provider { get; set; } = "Disabled";
    public string Profile { get; set; } = "default";
    public Uri? BaseUrl { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
}

public sealed class EmbeddingProviderOptions : AiProviderOptions
{
    [Range(1, 65536)] public int Dimensions { get; set; } = 4096;
    public string Version { get; set; } = "1";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "DatabaseProviders";
    public string DefaultProvider { get; set; } = "MySql";
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
}

public sealed class ConnectionCatalogOptions
{
    public const string SectionName = "ConnectionCatalog";
    public List<ConnectionOptions> Connections { get; set; } = [];
}

public sealed class ConnectionOptions
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string DisplayName { get; set; } = string.Empty;
    [Required] public string Provider { get; set; } = "MySql";
    public string? CredentialRef { get; set; }
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AccessPolicyOptions
{
    public const string SectionName = "AccessPolicy";
    public List<SubjectAccessPolicyOptions> Subjects { get; set; } = [];
}

public sealed class SubjectAccessPolicyOptions
{
    [Required] public string Subject { get; set; } = string.Empty;
    public List<ConnectionAccessOptions> Connections { get; set; } = [];
}

public sealed class ConnectionAccessOptions
{
    [Required] public string Id { get; set; } = string.Empty;
    public List<string> Objects { get; set; } = [];
}

public sealed class RagOptions
{
    public const string SectionName = "Rag";
    public string Mode { get; set; } = "LexicalOnly";
    [Range(1, 100)] public int TopK { get; set; } = 12;
    [Range(128, 32768)] public int TokenBudget { get; set; } = 4096;
}

public sealed class SqlSafetyOptions
{
    public const string SectionName = "SqlSafety";
    [Range(1, 5000)] public int DefaultRowLimit { get; set; } = 500;
    [Range(1, 5000)] public int MaximumRowLimit { get; set; } = 5000;
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
    [Range(1024, 5242880)] public int MaximumResultBytes { get; set; } = 5 * 1024 * 1024;
    public bool StrictLimitRequired { get; set; } = true;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string Mode { get; set; } = "DevelopmentHeader";
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public string RoleClaim { get; set; } = "roles";
    public string SubjectClaim { get; set; } = "sub";
    [Range(0, 300)] public int ClockSkewSeconds { get; set; } = 60;
    public DevelopmentIdentityOptions Development { get; set; } = new();
}

public sealed class DevelopmentIdentityOptions
{
    public string HeaderName { get; set; } = "X-SystemIQ-Development-Identity";
    public string Identity { get; set; } = "local-curator";
    public string DisplayName { get; set; } = "Local curator";
    public string[] Roles { get; set; } = ["DataIqGlossaryEditor"];
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "FileSystem";
    public FileSystemOptions FileSystem { get; set; } = new();
}

public sealed class FileSystemOptions { public string Root { get; set; } = "./data"; }

public sealed class HistoryOptions
{
    public const string SectionName = "History";
    [Range(1, 1000)] public int MaxEntries { get; set; } = 100;
}

public sealed class DenialStoreOptions
{
    public const string SectionName = "DenialStore";
    public string Provider { get; set; } = "SQLite";
    public string ConnectionString { get; set; } = "Data Source=./data/denials.db";
    [Range(1, 100)] public int MaximumDenials { get; set; } = 5;
    [Range(1, 1440)] public int WindowMinutes { get; set; } = 10;
}

public sealed class AuditOptions
{
    public const string SectionName = "Audit";
    public bool FailClosed { get; set; } = true;
}

public sealed class HealthOptions
{
    public const string SectionName = "Health";
    [Range(1, 5)] public int DependencyTimeoutSeconds { get; set; } = 2;
    [Range(1, 10)] public int AggregateTimeoutSeconds { get; set; } = 5;
}

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    [Range(1, 1000)] public int BatchSize { get; set; } = 100;
    [Range(1, 8)] public int MaximumAttempts { get; set; } = 3;
}

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public bool OpenTelemetryEnabled { get; set; }
    public string ServiceName { get; set; } = "SystemIQ.Api";
}

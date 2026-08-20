using System.Text.Json;
using SystemIQ.Functions.Models;

namespace SystemIQ.Functions.Services;

public sealed class ConnectionCatalog
{
    private sealed record Configuration(string Id, string DisplayName, string ConnectionString);

    private IReadOnlyList<Configuration> Load()
    {
        var json = Environment.GetEnvironmentVariable("DATABASE_CONNECTIONS_JSON") ?? "[]";
        return JsonSerializer.Deserialize<List<Configuration>>(json, JsonOptions.Default) ?? [];
    }

    public IReadOnlyList<ConnectionSummary> GetPermitted(AccessPolicy policy) =>
        Load()
            .Where(x => policy.Connections.Contains(x.Id))
            .Select(x => new ConnectionSummary(x.Id, x.DisplayName))
            .ToArray();

    public string GetConnectionString(string id) =>
        Load().SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.ConnectionString
        ?? throw new KeyNotFoundException($"Unknown database connection '{id}'.");
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}

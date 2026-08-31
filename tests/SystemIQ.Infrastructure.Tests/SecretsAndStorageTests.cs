using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SystemIQ.Infrastructure.Denials;
using SystemIQ.Infrastructure.Secrets;
using SystemIQ.Infrastructure.Storage;
using SystemIQ.Domain.Secrets;
using SystemIQ.Domain.Storage;
using SystemIQ.Domain.Queries;

namespace SystemIQ.Infrastructure.Tests;

public sealed class SecretsAndStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "systemiq-infra-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SecretResolver_UsesIndependentConfigurationAndRedactsSerialization()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:Chat"] = "token-value" }).Build();
        var resolver = new ConfigurationSecretResolver(configuration);

        using var secret = await resolver.ResolveAsync(new SecretReference("config:Secrets:Chat"));

        Assert.Equal("token-value", new string(secret.Reveal().Span));
        Assert.Equal("[REDACTED]", secret.ToString());
        Assert.DoesNotContain("token-value", JsonSerializer.Serialize(secret));
    }

    [Fact]
    public async Task SecretResolver_RefusesRelativeFile()
    {
        var resolver = new ConfigurationSecretResolver(new ConfigurationBuilder().Build());
        await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(new SecretReference("file:relative.secret")));
    }

    [Fact]
    public async Task FileStore_RejectsTraversal_AndEnforcesVersions()
    {
        var store = new FileSystemDocumentStore(_root);
        Assert.Throws<ArgumentException>(() => new DocumentKey("history", ["..", "secret"]));
        var key = new DocumentKey("history", ["user", "connection"]);
        var write = await store.WriteAsync(key, new Payload("one"), new(WriteConditionKind.Unconditional), default);
        var loaded = await store.ReadAsync<Payload>(key, default);
        Assert.Equal("one", loaded!.Value.Value);
        Assert.Equal(write.Version, loaded.Version);

        await Assert.ThrowsAsync<DocumentConflictException>(() => store.WriteAsync(key, new Payload("two"), new(WriteConditionKind.MatchVersion, "stale"), default));
        var next = await store.WriteAsync(key, new Payload("two"), new(WriteConditionKind.MatchVersion, write.Version), default);
        Assert.NotEqual(write.Version, next.Version);
    }

    [Fact]
    public async Task FileStore_AllowsOnlyOneConcurrentMatchingVersionWrite()
    {
        var store = new FileSystemDocumentStore(_root);
        var key = new DocumentKey("history", ["concurrent"]);
        var initial = await store.WriteAsync(key, new Payload("initial"),
            new(WriteConditionKind.Unconditional), default);

        var attempts = await Task.WhenAll(new[] { "one", "two" }.Select(async value =>
        {
            try
            {
                await store.WriteAsync(key, new Payload(value),
                    new(WriteConditionKind.MatchVersion, initial.Version), default);
                return true;
            }
            catch (DocumentConflictException) { return false; }
        }));

        Assert.Single(attempts, static succeeded => succeeded);
    }

    [Fact]
    public async Task QueryHistorySink_PersistsCompletedTurnWithoutUsingRawIdentifiersAsPaths()
    {
        var documents = new FileSystemDocumentStore(_root);
        var sink = new FileSystemQueryHistorySink(documents);
        var evaluation = new QueryEvaluationMetadata("mysql", "schema", "index", false, 2, new(3, 4));

        await sink.SaveAsync(new QueryHistoryEntry("unsafe/../correlation", "mysql:local", "question", "answer",
            "SELECT 1", 1, DateTimeOffset.UtcNow, evaluation), default);

        var stored = new List<Document<QueryHistoryEntry>>();
        await foreach (var document in documents.ListAsync<QueryHistoryEntry>(new DocumentPrefix("query-history", []), default))
            stored.Add(document);
        Assert.Single(stored);
        Assert.Equal("question", stored[0].Value.Question);
        Assert.DoesNotContain("unsafe", stored[0].Key.Segments[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecurityAuditSink_PersistsAppendOnlyAuditEvent()
    {
        var documents = new FileSystemDocumentStore(_root);
        var sink = new FileSystemSecurityAuditSink(documents);
        var audit = new SecurityAuditEvent("event-1", "connection_denied", "subject-a", "mysql-local",
            "correlation-1", "denied", DateTimeOffset.UtcNow);

        await sink.WriteAsync(audit, default);

        var stored = new List<Document<SecurityAuditEvent>>();
        await foreach (var document in documents.ListAsync<SecurityAuditEvent>(
            new DocumentPrefix("security-audit", []), default)) stored.Add(document);
        Assert.Single(stored);
        Assert.Equal("subject-a", stored[0].Value.Subject);
        await Assert.ThrowsAsync<DocumentConflictException>(() => sink.WriteAsync(audit, default));
    }

    [Fact]
    public async Task SqliteDenials_PersistRollingWindowAcrossInstances()
    {
        var path = Path.Combine(_root, "state", "denials.db");
        var first = new SqliteAccessDenialStore(path);
        await first.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        await first.RecordAsync("subject-a", now.AddMinutes(-10), default);
        await first.RecordAsync("subject-a", now, default);
        await first.RecordAsync("subject-b", now, default);

        var restarted = new SqliteAccessDenialStore(path);
        await restarted.InitializeAsync();
        var window = await restarted.GetWindowAsync("subject-a", now.AddMinutes(-1));

        Assert.Single(window);
        Assert.Equal("subject-a", window[0].Subject);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Payload(string Value);
}

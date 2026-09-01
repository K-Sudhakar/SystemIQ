using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Glossary;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Storage;
using SystemIQ.Application.Storage;
using SystemIQ.Application.Databases;
using SystemIQ.Domain.Rag;
using SystemIQ.Infrastructure.Glossary;
using SystemIQ.Infrastructure.Storage;

namespace SystemIQ.Infrastructure.Tests;

public sealed class CuratorStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "systemiq-curator-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Glossary_store_upserts_and_isolates_subjects_and_connections()
    {
        var documents = new FileSystemDocumentStore(_root);
        var store = new FileSystemGlossaryStore(documents);
        await store.UpsertAsync("subject-a", Entry("c1", "employees", "Employees"), default);
        await store.UpsertAsync("subject-a", Entry("c2", "orders", "Orders"), default);
        await store.UpsertAsync("subject-b", Entry("c1", "payroll", "Payroll"), default);
        await store.UpsertAsync("subject-a", Entry("c1", "employees", "Team members"), default);

        var loaded = await store.ListAsync("subject-a", "c1", 100, default);

        var entry = Assert.Single(loaded);
        Assert.Equal("Team members", entry.BusinessTerm);
        Assert.Equal("employees", entry.Table);
    }

    [Fact]
    public async Task Feedback_processing_excludes_positive_and_unauthorized_entries_and_supports_resolution()
    {
        var documents = new FileSystemDocumentStore(_root);
        var feedback = new FileSystemFeedbackSink(documents);
        var now = DateTimeOffset.UtcNow;
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c1", "m1", "down", "incorrect", "wrong", now), default);
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c1", "m2", "up", null, null, now), default);
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c2", "m3", "down", null, null, now), default);
        await feedback.SaveAsync(new FeedbackEntry("subject-b", "c1", "m4", "down", null, null, now), default);
        await SaveHistory(documents, "subject-a", "c1", "m1", "Question one", now);

        var processed = await feedback.ProcessAsync("subject-a", new HashSet<string>(["c1", "c2"]), default);
        var pending = await feedback.ListPendingAsync("subject-a", new HashSet<string>(["c1", "c2"]), "c1", 100, default);
        var authorizedOnly = await feedback.ListPendingAsync("subject-a", new HashSet<string>(["c1"]), null, 100, default);

        Assert.Equal(2, processed);
        var review = Assert.Single(pending);
        Assert.Single(authorizedOnly);
        Assert.Equal("Question one", review.Question);
        Assert.Equal("m1", review.MessageId);
        Assert.True(await feedback.ResolveAsync("subject-a", new HashSet<string>(["c1"]), review.Id, default));
        Assert.Empty(await feedback.ListPendingAsync("subject-a", new HashSet<string>(["c1"]), null, 100, default));
    }

    [Fact]
    public async Task Schema_glossary_defaults_use_provider_neutral_metadata_and_object_authorization()
    {
        var provider = new SchemaOnlyDatabaseProvider();
        var defaults = new SchemaGlossaryDefaultsProvider(new DatabaseProviderRegistry([provider]));
        var connection = new DatabaseConnection("c1", "Local", "mysql", "unused", new Dictionary<string, string>());

        var entries = await defaults.GetAsync(connection, new HashSet<string>(["employees"]), 100, default);

        var entry = Assert.Single(entries);
        Assert.Equal("employees", entry.Table);
        Assert.Equal("Employees", entry.BusinessTerm);
        Assert.Equal(["id", "name"], entry.RelatedColumns);
    }

    [Fact]
    public async Task Accuracy_reader_calculates_authorized_subject_metrics_only()
    {
        var documents = new FileSystemDocumentStore(_root);
        var feedback = new FileSystemFeedbackSink(documents);
        var now = DateTimeOffset.UtcNow;
        await SaveHistory(documents, "subject-a", "c1", "m1", "One", now.AddMinutes(-3));
        await SaveHistory(documents, "subject-a", "c1", "m2", "Two", now.AddMinutes(-2));
        await SaveHistory(documents, "subject-a", "c2", "m3", "Other connection", now.AddMinutes(-1));
        await SaveHistory(documents, "subject-b", "c1", "m4", "Other subject", now);
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c1", "m1", "up", null, null, now), default);
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c1", "m2", "down", null, null, now), default);
        await feedback.SaveAsync(new FeedbackEntry("subject-a", "c2", "m3", "up", null, null, now), default);

        var report = await new FileSystemAccuracyReportReader(documents).ReadAsync(
            "subject-a", new HashSet<string>(["c1"]), now.AddDays(-1), default);

        Assert.Equal(2, report.AnswerCount);
        Assert.Equal(2, report.RatedCount);
        Assert.Equal(50, report.ThumbsUpRate);
        Assert.Equal(50, report.ThumbsDownRate);
        Assert.Equal(100, report.FeedbackCoverage);
    }

    private static GlossaryEntry Entry(string connection, string table, string term) =>
        new(connection, table, term, "Description", [], [], []);

    private static Task SaveHistory(IObjectDocumentStore documents, string subject, string connection,
        string correlation, string question, DateTimeOffset completedAt)
    {
        var evaluation = new QueryEvaluationMetadata("mysql", "schema", "index", false, 1, new(1, 1));
        return new FileSystemQueryHistorySink(documents).SaveAsync(new QueryHistoryEntry(
            correlation, connection, question, "Answer", "SELECT 1", 1, completedAt, evaluation, subject), default);
    }

    private sealed class SchemaOnlyDatabaseProvider : IDatabaseProvider, ISchemaIntrospector
    {
        public string ProviderId => "mysql";
        public DatabaseCapabilities Capabilities => DatabaseCapabilities.SchemaDiscovery;
        public ISchemaIntrospector Schema => this;
        public ISqlDialect Dialect => throw new NotSupportedException();
        public ISqlValidator Validator => throw new NotSupportedException();
        public IReadOnlyQueryExecutor Executor => throw new NotSupportedException();
        public Task<SchemaSnapshot> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken) =>
            Task.FromResult(new SchemaSnapshot("mysql", "8", "catalog", null,
                [
                    new SchemaTable("catalog", null, "employees", [
                        new SchemaColumn("id", "int", false, 1, true),
                        new SchemaColumn("name", "varchar", false, 2)]),
                    new SchemaTable("catalog", null, "payroll", [new SchemaColumn("salary", "decimal", false, 1)])
                ], [], "hash", DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

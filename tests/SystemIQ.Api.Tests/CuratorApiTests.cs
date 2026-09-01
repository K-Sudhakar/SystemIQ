using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemIQ.Application.Feedback;
using SystemIQ.Application.Glossary;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Glossary;

namespace SystemIQ.Api.Tests;

public sealed class CuratorApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Glossary_returns_an_empty_array_when_no_curated_entries_exist()
    {
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IGlossaryStore>();
            services.AddSingleton<IGlossaryStore>(new TestGlossaryStore([]));
        });
        using var client = AuthorizedClient(customFactory);

        var response = await client.GetAsync("/api/curation/glossary/mysql-local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Glossary_gets_return_empty_arrays_and_authorized_entries()
    {
        var entry = Entry("mysql-local", "employees", "Employees");
        var store = new TestGlossaryStore([entry]);
        var defaults = new TestGlossaryDefaultsProvider([]);
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IGlossaryStore>();
            services.RemoveAll<IGlossaryDefaultsProvider>();
            services.AddSingleton<IGlossaryStore>(store);
            services.AddSingleton<IGlossaryDefaultsProvider>(defaults);
        });
        using var client = AuthorizedClient(customFactory);

        var curated = await client.GetAsync("/api/curation/glossary/mysql-local");
        var generated = await client.GetAsync("/api/curation/glossary/mysql-local/defaults");

        Assert.Equal(HttpStatusCode.OK, curated.StatusCode);
        Assert.Contains("Employees", await curated.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("[]", await generated.Content.ReadAsStringAsync());
        Assert.Equal("local-curator", store.Subject);
        Assert.Equal("mysql-local", store.ConnectionId);
    }

    [Fact]
    public async Task Glossary_put_adds_or_updates_the_frontend_contract()
    {
        var store = new TestGlossaryStore([]);
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IGlossaryStore>();
            services.AddSingleton<IGlossaryStore>(store);
        });
        using var client = AuthorizedClient(customFactory);
        var entry = Entry("mysql-local", "employees", "Team members");

        var response = await client.PutAsJsonAsync("/api/curation/glossary/mysql-local/employees", entry);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<GlossaryEntry>();
        Assert.Equal(entry.ConnectionId, saved!.ConnectionId);
        Assert.Equal(entry.Table, saved.Table);
        Assert.Equal(entry.BusinessTerm, saved.BusinessTerm);
        Assert.Equal(entry.Synonyms, saved.Synonyms);
        Assert.Equal(entry.Table, store.Saved!.Table);
        Assert.Equal("local-curator", store.Subject);
    }

    [Theory]
    [InlineData("/api/curation/glossary/not-authorized")]
    [InlineData("/api/curation/glossary/not-authorized/defaults")]
    public async Task Glossary_rejects_unauthorized_connections_without_reading_storage(string path)
    {
        var store = new TestGlossaryStore([]);
        var defaults = new TestGlossaryDefaultsProvider([]);
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IGlossaryStore>();
            services.RemoveAll<IGlossaryDefaultsProvider>();
            services.AddSingleton<IGlossaryStore>(store);
            services.AddSingleton<IGlossaryDefaultsProvider>(defaults);
        });
        using var client = AuthorizedClient(customFactory);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("connection_denied", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(store.WasRead);
        Assert.False(defaults.WasRead);
    }

    [Fact]
    public async Task Glossary_rejects_invalid_or_mismatched_input()
    {
        var store = new TestGlossaryStore([]);
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IGlossaryStore>();
            services.AddSingleton<IGlossaryStore>(store);
        });
        using var client = AuthorizedClient(customFactory);
        var invalid = Entry("mysql-local", "other-table", new string('t', 201));

        var response = await client.PutAsJsonAsync("/api/curation/glossary/mysql-local/employees", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_glossary_entry", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Feedback_inbox_supports_empty_filtered_processing_and_resolution_contracts()
    {
        var review = new FeedbackReviewEntry("review-1", "local-curator", "mysql-local", "answer-1",
            "Question", [], [], "incorrect", "Wrong", DateTimeOffset.UtcNow, false);
        var store = new TestFeedbackReviewStore([review]) { Processed = 1, ResolveResult = true };
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IFeedbackReviewStore>();
            services.AddSingleton<IFeedbackReviewStore>(store);
        });
        using var client = AuthorizedClient(customFactory);

        var all = await client.GetAsync("/api/curation/feedback");
        var filtered = await client.GetAsync("/api/curation/feedback?connectionId=mysql-local");
        var processed = await client.PostAsync("/api/curation/feedback/process", null);
        var resolved = await client.PostAsync("/api/curation/feedback/review-1/resolve", null);

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Contains("Question", await all.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Equal("mysql-local", store.FilterConnectionId);
        Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
        Assert.Contains("\"processed\":1", await processed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, resolved.StatusCode);
        Assert.Equal("review-1", store.ResolvedId);
    }

    [Fact]
    public async Task Feedback_inbox_returns_empty_and_rejects_unauthorized_filter_without_storage_access()
    {
        var store = new TestFeedbackReviewStore([]);
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IFeedbackReviewStore>();
            services.AddSingleton<IFeedbackReviewStore>(store);
        });
        using var client = AuthorizedClient(customFactory);

        Assert.Equal("[]", await (await client.GetAsync("/api/curation/feedback")).Content.ReadAsStringAsync());
        store.WasRead = false;
        var denied = await client.GetAsync("/api/curation/feedback?connectionId=not-authorized");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(store.WasRead);
    }

    [Fact]
    public async Task Accuracy_report_returns_frontend_zero_shape_and_honors_refresh_days()
    {
        var reader = new TestAccuracyReportReader(new AccuracyReport(0, 0, 0, 0, 0, null, null));
        using var customFactory = WithServices(services =>
        {
            services.RemoveAll<IAccuracyReportReader>();
            services.AddSingleton<IAccuracyReportReader>(reader);
        });
        using var client = AuthorizedClient(customFactory);

        var first = await client.GetAsync("/api/curation/accuracy-report?days=30");
        var refresh = await client.GetAsync("/api/curation/accuracy-report?days=7");
        using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(0, document.RootElement.GetProperty("thumbsUpRate").GetDouble());
        Assert.Equal(0, document.RootElement.GetProperty("thumbsDownRate").GetDouble());
        Assert.Equal(0, document.RootElement.GetProperty("feedbackCoverage").GetDouble());
        Assert.Equal(0, document.RootElement.GetProperty("answerCount").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("ratedCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(7, reader.LastDays);
        Assert.Equal("local-curator", reader.Subject);
        Assert.Contains("mysql-local", reader.ConnectionIds);
    }

    private WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configure) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(configure));

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");
        return client;
    }

    private static GlossaryEntry Entry(string connectionId, string table, string term) =>
        new(connectionId, table, term, "Description", ["staff"], ["name"], []);

    private sealed class TestGlossaryStore(IReadOnlyList<GlossaryEntry> entries) : IGlossaryStore
    {
        public bool WasRead { get; private set; }
        public string? Subject { get; private set; }
        public string? ConnectionId { get; private set; }
        public GlossaryEntry? Saved { get; private set; }
        public Task<IReadOnlyList<GlossaryEntry>> ListAsync(string subject, string connectionId, int maxEntries, CancellationToken ct)
        { WasRead = true; Subject = subject; ConnectionId = connectionId; return Task.FromResult(entries); }
        public Task<GlossaryEntry> UpsertAsync(string subject, GlossaryEntry entry, CancellationToken ct)
        { Subject = subject; Saved = entry; return Task.FromResult(entry); }
    }

    private sealed class TestGlossaryDefaultsProvider(IReadOnlyList<GlossaryEntry> entries) : IGlossaryDefaultsProvider
    {
        public bool WasRead { get; private set; }
        public Task<IReadOnlyList<GlossaryEntry>> GetAsync(DatabaseConnection connection, IReadOnlySet<string> authorizedObjects,
            int maxEntries, CancellationToken ct)
        { WasRead = true; return Task.FromResult(entries); }
    }

    private sealed class TestFeedbackReviewStore(IReadOnlyList<FeedbackReviewEntry> entries) : IFeedbackReviewStore
    {
        public bool WasRead { get; set; }
        public string? FilterConnectionId { get; private set; }
        public string? ResolvedId { get; private set; }
        public int Processed { get; init; }
        public bool ResolveResult { get; init; }
        public Task<IReadOnlyList<FeedbackReviewEntry>> ListPendingAsync(string subject, IReadOnlySet<string> connectionIds,
            string? connectionId, int maxEntries, CancellationToken ct)
        { WasRead = true; FilterConnectionId = connectionId; return Task.FromResult(entries); }
        public Task<int> ProcessAsync(string subject, IReadOnlySet<string> connectionIds, CancellationToken ct) => Task.FromResult(Processed);
        public Task<bool> ResolveAsync(string subject, IReadOnlySet<string> connectionIds, string id, CancellationToken ct)
        { ResolvedId = id; return Task.FromResult(ResolveResult); }
    }

    private sealed class TestAccuracyReportReader(AccuracyReport report) : IAccuracyReportReader
    {
        public string? Subject { get; private set; }
        public IReadOnlySet<string> ConnectionIds { get; private set; } = new HashSet<string>();
        public int LastDays { get; private set; }
        public Task<AccuracyReport> ReadAsync(string subject, IReadOnlySet<string> connectionIds,
            DateTimeOffset since, CancellationToken ct)
        { Subject = subject; ConnectionIds = connectionIds; LastDays = (int)Math.Round((DateTimeOffset.UtcNow - since).TotalDays); return Task.FromResult(report); }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SystemIQ.Application.Queries;
using SystemIQ.Application.AI;
using SystemIQ.Application.Databases;
using SystemIQ.Application.Rag;
using SystemIQ.Application.Secrets;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.AI;
using SystemIQ.Infrastructure.AI;
using SystemIQ.Domain.Databases;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Rag;
using SystemIQ.Domain.Storage;

namespace SystemIQ.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionCatalog:Connections:0:Id"] = "mysql-local",
            ["ConnectionCatalog:Connections:0:DisplayName"] = "Local MySQL",
            ["ConnectionCatalog:Connections:0:Provider"] = "MySql",
            ["ConnectionCatalog:Connections:0:CredentialRef"] = "config:Secrets:MySql",
            ["Secrets:MySql"] = "Server=example.invalid;Database=test;User ID=readonly;Password=test-only",
            ["AccessPolicy:Subjects:0:Subject"] = "local-curator",
            ["AccessPolicy:Subjects:0:Connections:0:Id"] = "mysql-local",
            ["AccessPolicy:Subjects:0:Connections:0:Objects:0"] = "*"
        }));
    }

}

public sealed class ApiHostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Portable_host_registers_the_real_pipeline_and_foundational_providers()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<NaturalLanguageQueryOrchestrator>(services.GetRequiredService<INaturalLanguageQueryOrchestrator>());
        Assert.NotNull(services.GetRequiredService<ISecretResolver>());
        Assert.Equal("mysql", services.GetRequiredService<IDatabaseProviderRegistry>().GetRequired("MySql").ProviderId);
        Assert.NotNull(services.GetRequiredService<IRagRetriever>());
        Assert.Throws<SystemIQ.Application.AI.UnknownProviderException>(() =>
            services.GetRequiredService<IChatProviderRegistry>().GetRequired("default"));
    }

    [Fact]
    public async Task Access_policy_is_deny_by_default_and_honors_explicit_connections()
    {
        using var deniedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccessPolicy:Subjects:0:Subject"] = "somebody-else"
            })));
        using var client = deniedFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");

        var response = await client.GetAsync("/api/connections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("mysql-local", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_ai_configuration_selects_independent_hosts_and_profiles()
    {
        using var configuredFactory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Chat:Provider"] = "OpenAICompatible",
                ["AI:Chat:Profile"] = "chat-local",
                ["AI:Chat:BaseUrl"] = "https://chat.example/",
                ["AI:Chat:Model"] = "chat-model",
                ["AI:Embeddings:Provider"] = "OpenAICompatible",
                ["AI:Embeddings:Profile"] = "embed-local",
                ["AI:Embeddings:BaseUrl"] = "https://embeddings.example/",
                ["AI:Embeddings:Model"] = "embedding-model",
                ["AI:Embeddings:Dimensions"] = "2"
            })));
        using var scope = configuredFactory.Services.CreateScope();

        var chat = Assert.IsType<SecretResolvingOpenAiChatProvider>(
            scope.ServiceProvider.GetRequiredService<IChatProviderRegistry>().GetRequired("chat-local"));
        var embeddings = Assert.IsType<SecretResolvingOpenAiEmbeddingProvider>(
            scope.ServiceProvider.GetRequiredService<IEmbeddingProviderRegistry>().GetRequired("embed-local"));
        Assert.Equal("https://chat.example/", chat.BaseUrl.ToString());
        Assert.Equal("https://embeddings.example/", embeddings.BaseUrl.ToString());
        Assert.NotEqual(chat.BaseUrl, embeddings.BaseUrl);
    }

    [Fact]
    public async Task Registered_orchestrator_completes_with_mocked_ai_and_database()
    {
        var chatHandler = new PipelineAiHandler();
        var embeddingHandler = new PipelineAiHandler();
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Chat:Provider"] = "OpenAICompatible", ["AI:Chat:Profile"] = "chat-local",
                ["AI:Chat:BaseUrl"] = "https://chat.example/", ["AI:Chat:Model"] = "chat-model",
                ["AI:Embeddings:Provider"] = "OpenAICompatible", ["AI:Embeddings:Profile"] = "embed-local",
                ["AI:Embeddings:BaseUrl"] = "https://embeddings.example/", ["AI:Embeddings:Model"] = "embedding-model",
                ["AI:Embeddings:Dimensions"] = "2"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseProvider>();
                services.AddSingleton<IDatabaseProvider, PipelineDatabaseProvider>();
                services.RemoveAll<IQueryHistorySink>();
                services.AddSingleton<RecordingHistorySink>();
                services.AddSingleton<IQueryHistorySink>(provider => provider.GetRequiredService<RecordingHistorySink>());
                services.AddHttpClient("SystemIQ.Chat").ConfigurePrimaryHttpMessageHandler(() => chatHandler);
                services.AddHttpClient("SystemIQ.Embeddings").ConfigurePrimaryHttpMessageHandler(() => embeddingHandler);
            });
        });
        using var scope = configuredFactory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<INaturalLanguageQueryOrchestrator>();
        var request = new NaturalLanguageQueryRequest("List widgets",
            new DatabaseConnection("mysql-local", "Local MySQL", "mysql", "unused", new Dictionary<string, string>()),
            "chat-local", "embed-local", new HashSet<string>(["*"]), "correlation", 5, 500);

        var result = await orchestrator.ExecuteAsync(request, default);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal("One widget was returned.", result.Answer);
        Assert.Single(result.Rows!.Rows);
        Assert.Contains(chatHandler.Paths, x => x == "/v1/chat/completions");
        Assert.Contains(embeddingHandler.Paths, x => x == "/v1/embeddings");
        Assert.True(scope.ServiceProvider.GetRequiredService<RecordingHistorySink>().WasSaved);
    }

    [Theory]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/startup")]
    [InlineData("/api/health/ready")]
    public async Task Health_endpoints_are_anonymous_and_healthy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connections_require_the_exact_fixed_development_identity()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/connections")).StatusCode);
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "arbitrary-user");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/connections")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-SystemIQ-Development-Identity");
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");
        var response = await client.GetAsync("/api/connections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("mysql-local", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Credential", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_chat_request_returns_problem_details_with_stable_code()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { connectionId = "", question = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("invalid_chat_request", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denied_chat_is_audited_and_counted_before_returning_forbidden()
    {
        using var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISecurityAuditSink>();
            services.RemoveAll<IAccessDenialStore>();
            services.AddSingleton<RecordingAuditSink>();
            services.AddSingleton<ISecurityAuditSink>(provider => provider.GetRequiredService<RecordingAuditSink>());
            services.AddSingleton<RecordingDenialStore>();
            services.AddSingleton<IAccessDenialStore>(provider => provider.GetRequiredService<RecordingDenialStore>());
        }));
        using var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");

        var response = await client.PostAsJsonAsync("/api/chat/stream",
            new { connectionId = "not-authorized", question = "Count records" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("connection_denied", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("connection_denied", customFactory.Services.GetRequiredService<RecordingAuditSink>().Last?.EventType);
        Assert.Equal(1, customFactory.Services.GetRequiredService<RecordingDenialStore>().Records);
    }

    [Fact]
    public async Task Chat_stream_preserves_the_status_answer_rows_complete_contract()
    {
        using var customFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<INaturalLanguageQueryOrchestrator>();
            services.AddSingleton<INaturalLanguageQueryOrchestrator, SuccessfulOrchestrator>();
        }));
        using var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SystemIQ-Development-Identity", "local-curator");
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { connectionId = "mysql-local", question = "Count records" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("event: status", body, StringComparison.Ordinal);
        Assert.Contains("event: answer", body, StringComparison.Ordinal);
        Assert.Contains("event: rows", body, StringComparison.Ordinal);
        Assert.Contains("event: complete", body, StringComparison.Ordinal);
    }

    private sealed class SuccessfulOrchestrator : INaturalLanguageQueryOrchestrator
    {
        public Task<NaturalLanguageQueryResult> ExecuteAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken)
        {
            var rows = new QueryRows(["count"], [new Dictionary<string, object?> { ["count"] = 3 }], false, 12);
            return Task.FromResult(new NaturalLanguageQueryResult(true, "There are three records.", rows, "SELECT COUNT(*)", null, null));
        }
    }

    private sealed class PipelineAiHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        private int _chatCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var count = document.RootElement.GetProperty("input").GetArrayLength();
                var data = string.Join(',', Enumerable.Range(0, count).Select(i => $"{{\"index\":{i},\"embedding\":[1,0]}}"));
                return Json($"{{\"data\":[{data}]}}");
            }
            var content = Interlocked.Increment(ref _chatCalls) == 1 ? "SELECT `id` FROM `widgets`" : "One widget was returned.";
            return Json(JsonSerializer.Serialize(new
            {
                model = "chat-model",
                choices = new[] { new { message = new { content }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 1, completion_tokens = 1 }
            }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private sealed class PipelineDatabaseProvider : IDatabaseProvider
    {
        public string ProviderId => "mysql";
        public DatabaseCapabilities Capabilities => DatabaseCapabilities.All;
        public ISchemaIntrospector Schema { get; } = new PipelineSchema();
        public ISqlDialect Dialect { get; } = new PipelineDialect();
        public ISqlValidator Validator { get; } = new PipelineValidator();
        public IReadOnlyQueryExecutor Executor { get; } = new PipelineExecutor();

        private sealed class PipelineSchema : ISchemaIntrospector
        {
            public Task<SchemaSnapshot> DiscoverAsync(DatabaseConnection connection, CancellationToken cancellationToken) =>
                Task.FromResult(new SchemaSnapshot("mysql", "8", "catalog", null,
                    [new SchemaTable("catalog", null, "widgets", [new SchemaColumn("id", "int", false, 1, true)])],
                    [], "schema-hash", DateTimeOffset.UtcNow));
        }
        private sealed class PipelineDialect : ISqlDialect
        {
            public string IdentifierQuote => "`";
            public string BuildSqlGenerationGuidance(SchemaSnapshot schema, IReadOnlyList<RagContextChunk> context) => "Use MySQL.";
        }
        private sealed class PipelineValidator : ISqlValidator
        {
            public Task<SqlValidationResult> ValidateAsync(string sql, SqlValidationContext context, CancellationToken cancellationToken) =>
                Task.FromResult(SqlValidationResult.Allow(sql + " LIMIT 500", [new SqlObjectReference(null, null, "widgets")]));
        }
        private sealed class PipelineExecutor : IReadOnlyQueryExecutor
        {
            public Task<QueryRows> ExecuteAsync(ValidatedSql query, DatabaseConnection connection, QueryExecutionLimits limits, CancellationToken cancellationToken) =>
                Task.FromResult(new QueryRows(["id"], [new Dictionary<string, object?> { ["id"] = 1 }], false, 8));
        }
    }

    private sealed class RecordingHistorySink : IQueryHistorySink
    {
        public bool WasSaved { get; private set; }
        public Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken)
        { WasSaved = true; return Task.CompletedTask; }
    }

    private sealed class RecordingAuditSink : ISecurityAuditSink
    {
        public SecurityAuditEvent? Last { get; private set; }
        public Task WriteAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken)
        { Last = auditEvent; return Task.CompletedTask; }
    }

    private sealed class RecordingDenialStore : IAccessDenialStore
    {
        public int Records { get; private set; }
        public Task RecordAsync(string subject, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        { Records++; return Task.CompletedTask; }
        public Task<IReadOnlyList<AccessDenialEvent>> GetWindowAsync(string subject, DateTimeOffset since,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AccessDenialEvent>>([]);
    }
}

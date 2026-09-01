using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SystemIQ.Api.Options;
using SystemIQ.Api.Security;
using SystemIQ.Api.Services;
using SystemIQ.Application.Databases;
using SystemIQ.Application.AI;
using SystemIQ.Application.Feedback;
using SystemIQ.Application.Glossary;
using SystemIQ.Application.Queries;
using SystemIQ.Application.Rag;
using SystemIQ.Application.Secrets;
using SystemIQ.Application.Storage;
using SystemIQ.Domain.Queries;
using SystemIQ.Domain.Feedback;
using SystemIQ.Domain.Glossary;
using SystemIQ.Domain.Storage;
using SystemIQ.Infrastructure.AI;
using SystemIQ.Infrastructure.Databases;
using SystemIQ.Infrastructure.Denials;
using SystemIQ.Infrastructure.Glossary;
using SystemIQ.Infrastructure.Rag;
using SystemIQ.Infrastructure.Secrets;
using SystemIQ.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", "request_failed");
});

BindOptions(builder.Services, builder.Configuration);
builder.Services.AddSingleton<IValidateOptions<AiOptions>, SystemIqProfileValidator>();
builder.Services.AddOptions<AiOptions>().ValidateOnStart();

var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
if (auth.Mode.Equals("DevelopmentHeader", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAuthentication(DevelopmentHeaderAuthenticationHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(
            DevelopmentHeaderAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = auth.Authority;
        options.Audience = auth.Audience;
        options.TokenValidationParameters.RoleClaimType = auth.RoleClaim;
        options.TokenValidationParameters.NameClaimType = auth.SubjectClaim;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(auth.ClockSkewSeconds);
    });
}
builder.Services.AddAuthorization(options => options.AddPolicy("Curator", policy => policy.RequireRole("DataIqGlossaryEditor")));
builder.Services.AddHealthChecks()
    .AddCheck("process", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck("configuration", () => HealthCheckResult.Healthy(), tags: ["startup", "ready"]);
builder.Services.TryAddSingleton<IConnectionCatalog, ConfigurationConnectionCatalog>();
builder.Services.TryAddSingleton<IConnectionAccessPolicyProvider, ConfigurationConnectionAccessPolicyProvider>();
builder.Services.TryAddSingleton<ISecretResolver, ConfigurationSecretResolver>();
builder.Services.TryAddSingleton<MySqlConnectionFactory>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDatabaseProvider, MySqlDatabaseProvider>());
builder.Services.TryAddSingleton<IDatabaseProviderRegistry, DatabaseProviderRegistry>();
builder.Services.AddHttpClient("SystemIQ.Chat");
builder.Services.AddHttpClient("SystemIQ.Embeddings");
builder.Services.TryAddSingleton<IChatProviderRegistry, ConfigurationChatProviderRegistry>();
builder.Services.TryAddSingleton<IEmbeddingProviderRegistry, ConfigurationEmbeddingProviderRegistry>();
builder.Services.TryAddSingleton<InMemoryRagIndex>();
builder.Services.TryAddSingleton<IRagIndex>(services => services.GetRequiredService<InMemoryRagIndex>());
builder.Services.TryAddSingleton<IRagIndexStore>(services => services.GetRequiredService<InMemoryRagIndex>());
builder.Services.TryAddSingleton(services =>
{
    var ai = services.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings;
    return new SchemaRagIndexer(services.GetRequiredService<IEmbeddingProviderRegistry>(),
        services.GetRequiredService<IRagIndexStore>(),
        new SchemaRagIndexOptions(ai.Profile, ai.Model, ai.Version));
});
builder.Services.TryAddSingleton(services => new VectorRagRetriever(
    services.GetRequiredService<IRagIndex>(), services.GetRequiredService<IEmbeddingProviderRegistry>()));
builder.Services.TryAddSingleton<IRagRetriever, SchemaGroundingRagRetriever>();
builder.Services.TryAddSingleton<IObjectDocumentStore>(services => new FileSystemDocumentStore(
    services.GetRequiredService<IOptions<StorageOptions>>().Value.FileSystem.Root));
builder.Services.TryAddSingleton(services => new SqliteAccessDenialStore(
    services.GetRequiredService<IOptions<DenialStoreOptions>>().Value.ConnectionString));
builder.Services.TryAddSingleton<IAccessDenialStore>(services => services.GetRequiredService<SqliteAccessDenialStore>());
builder.Services.TryAddSingleton<ISecurityAuditSink, FileSystemSecurityAuditSink>();
builder.Services.TryAddSingleton<FileSystemQueryHistorySink>();
builder.Services.TryAddSingleton<IQueryHistorySink>(services => services.GetRequiredService<FileSystemQueryHistorySink>());
builder.Services.TryAddSingleton<IQueryHistoryReader>(services => services.GetRequiredService<FileSystemQueryHistorySink>());
builder.Services.TryAddSingleton<FileSystemFeedbackSink>();
builder.Services.TryAddSingleton<IFeedbackSink>(services => services.GetRequiredService<FileSystemFeedbackSink>());
builder.Services.TryAddSingleton<IFeedbackReviewStore>(services => services.GetRequiredService<FileSystemFeedbackSink>());
builder.Services.TryAddSingleton<IAccuracyReportReader, FileSystemAccuracyReportReader>();
builder.Services.TryAddSingleton<IGlossaryStore, FileSystemGlossaryStore>();
builder.Services.TryAddSingleton<IGlossaryDefaultsProvider, SchemaGlossaryDefaultsProvider>();
builder.Services.TryAddSingleton(services =>
{
    var safety = services.GetRequiredService<IOptions<SqlSafetyOptions>>().Value;
    return new SystemIQ.Domain.Databases.QueryExecutionLimits(
        Math.Min(safety.DefaultRowLimit, safety.MaximumRowLimit), safety.MaximumResultBytes,
        TimeSpan.FromSeconds(safety.TimeoutSeconds));
});
builder.Services.TryAddSingleton<INaturalLanguageQueryOrchestrator, NaturalLanguageQueryOrchestrator>();

var app = builder.Build();
await app.Services.GetRequiredService<SqliteAccessDenialStore>().InitializeAsync();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

MapHealth("/api/health/live", "live");
MapHealth("/api/health/startup", "startup");
MapHealth("/api/health/ready", "ready");

app.MapGet("/api/health/config", (IOptions<StorageOptions> storage, IOptions<DenialStoreOptions> denial,
    IOptions<DatabaseOptions> database, IOptions<AiOptions> ai, IOptions<AuthOptions> authOptions) =>
    Results.Ok(ConfigurationDiagnostics.Create(storage.Value, denial.Value, database.Value, ai.Value, authOptions.Value)))
    .RequireAuthorization("Curator");

app.MapGet("/api/connections", async (IConnectionCatalog catalog, IConnectionAccessPolicyProvider policies,
    IOptions<AuthOptions> authOptions, HttpContext context, CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    return Results.Ok(await catalog.ListPermittedAsync(policy, ct));
}).RequireAuthorization();

app.MapGet("/api/history/{connectionId}", async (
    string connectionId,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IQueryHistoryReader history,
    IOptions<HistoryOptions> historyOptions,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    var connection = policy.CanAccessConnection(connectionId)
        ? await catalog.FindAsync(connectionId, ct)
        : null;
    if (connection is null)
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Connection unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "connection_denied" });

    var entries = await history.ReadAsync(subject, connection.Id, historyOptions.Value.MaxEntries, ct);
    var messages = entries.SelectMany(entry => new[]
    {
        new HistoryMessageContract($"{entry.CorrelationId}:user", "user", entry.Question, entry.CompletedAt),
        new HistoryMessageContract(entry.CorrelationId, "assistant", entry.Answer, entry.CompletedAt)
    });
    return Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/api/feedback", async (
    FeedbackRequestContract command,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IFeedbackSink feedback,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var validationError = FeedbackRequestRules.Validate(
        command.ConnectionId, command.MessageId, command.Rating, command.Reason, command.Comment);
    if (validationError is not null)
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid feedback request",
            detail: validationError,
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_feedback_request" });

    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    var connection = policy.CanAccessConnection(command.ConnectionId!)
        ? await catalog.FindAsync(command.ConnectionId!, ct)
        : null;
    if (connection is null)
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Connection unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "connection_denied" });

    await feedback.SaveAsync(new FeedbackEntry(
        subject,
        connection.Id,
        command.MessageId!.Trim(),
        command.Rating!,
        FeedbackRequestRules.NormalizeOptional(command.Reason),
        FeedbackRequestRules.NormalizeOptional(command.Comment),
        TimeProvider.System.GetUtcNow()), ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/curation/glossary/{connectionId}", async (
    string connectionId,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IGlossaryStore glossary,
    IOptions<CurationOptions> curationOptions,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    var connection = policy.CanAccessConnection(connectionId) ? await catalog.FindAsync(connectionId, ct) : null;
    if (connection is null) return ConnectionDenied();
    var authorizedObjects = policy.ObjectsFor(connection.Id);
    var entries = await glossary.ListAsync(subject, connection.Id, curationOptions.Value.MaxGlossaryEntries, ct);
    return Results.Ok(entries.Where(entry => GlossaryEntryRules.IsAuthorizedTable(entry.Table, authorizedObjects)));
}).RequireAuthorization("Curator");

app.MapGet("/api/curation/glossary/{connectionId}/defaults", async (
    string connectionId,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IGlossaryDefaultsProvider defaults,
    IOptions<CurationOptions> curationOptions,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    var connection = policy.CanAccessConnection(connectionId) ? await catalog.FindAsync(connectionId, ct) : null;
    if (connection is null) return ConnectionDenied();
    return Results.Ok(await defaults.GetAsync(connection, policy.ObjectsFor(connection.Id),
        curationOptions.Value.MaxGlossaryEntries, ct));
}).RequireAuthorization("Curator");

app.MapPut("/api/curation/glossary/{connectionId}/{table}", async (
    string connectionId,
    string table,
    GlossaryEntry command,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IGlossaryStore glossary,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var validationError = GlossaryEntryRules.Validate(command);
    if (validationError is not null || !command.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase) ||
        !command.Table.Equals(table, StringComparison.OrdinalIgnoreCase))
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid glossary entry",
            detail: validationError ?? "Route and payload identifiers must match.",
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_glossary_entry" });

    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    var connection = policy.CanAccessConnection(connectionId) ? await catalog.FindAsync(connectionId, ct) : null;
    if (connection is null || !GlossaryEntryRules.IsAuthorizedTable(command.Table, policy.ObjectsFor(connectionId)))
        return ConnectionDenied();
    var normalized = GlossaryEntryRules.Normalize(command, connection.Id);
    return Results.Ok(await glossary.UpsertAsync(subject, normalized, ct));
}).RequireAuthorization("Curator");

app.MapGet("/api/curation/feedback", async (
    string? connectionId,
    IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies,
    IFeedbackReviewStore reviews,
    IOptions<CurationOptions> curationOptions,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    if (!string.IsNullOrWhiteSpace(connectionId) &&
        (!policy.CanAccessConnection(connectionId) || await catalog.FindAsync(connectionId, ct) is null))
        return ConnectionDenied();
    var entries = await reviews.ListPendingAsync(subject, policy.ConnectionIds, connectionId,
        curationOptions.Value.MaxFeedbackReviewEntries, ct);
    return Results.Ok(entries.Select(ToFeedbackReviewContract));
}).RequireAuthorization("Curator");

app.MapPost("/api/curation/feedback/process", async (
    IConnectionAccessPolicyProvider policies,
    IFeedbackReviewStore reviews,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    return Results.Ok(new { processed = await reviews.ProcessAsync(subject, policy.ConnectionIds, ct) });
}).RequireAuthorization("Curator");

app.MapPost("/api/curation/feedback/{id}/resolve", async (
    string id,
    IConnectionAccessPolicyProvider policies,
    IFeedbackReviewStore reviews,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    return await reviews.ResolveAsync(subject, policy.ConnectionIds, id, ct)
        ? Results.NoContent()
        : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Feedback review unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "feedback_review_unavailable" });
}).RequireAuthorization("Curator");

app.MapGet("/api/curation/accuracy-report", async (
    int days,
    IConnectionAccessPolicyProvider policies,
    IAccuracyReportReader reports,
    IOptions<CurationOptions> curationOptions,
    IOptions<AuthOptions> authOptions,
    HttpContext context,
    CancellationToken ct) =>
{
    if (days <= 0 || days > curationOptions.Value.MaximumAccuracyDays)
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid accuracy report range",
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_accuracy_range" });
    var subject = GetSubject(context, authOptions.Value);
    var policy = await policies.GetAsync(subject, ct);
    return Results.Ok(await reports.ReadAsync(subject, policy.ConnectionIds,
        TimeProvider.System.GetUtcNow().AddDays(-days), ct));
}).RequireAuthorization("Curator");

app.MapPost("/api/chat/stream", async (ChatRequestContract command, IConnectionCatalog catalog,
    IConnectionAccessPolicyProvider policies, INaturalLanguageQueryOrchestrator orchestrator,
    IOptions<AiOptions> aiOptions, IOptions<RagOptions> ragOptions, IOptions<AuthOptions> authOptions,
    IOptions<DenialStoreOptions> denialOptions, IOptions<AuditOptions> auditOptions,
    IAccessDenialStore denials, ISecurityAuditSink audits,
    HttpContext context, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(command.ConnectionId) || string.IsNullOrWhiteSpace(command.Question) || command.Question.Length > 4000)
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid chat request", extensions: new Dictionary<string, object?> { ["code"] = "invalid_chat_request" });

    var subject = GetSubject(context, authOptions.Value);
    var window = await denials.GetWindowAsync(subject,
        DateTimeOffset.UtcNow.AddMinutes(-denialOptions.Value.WindowMinutes), ct);
    if (window.Count >= denialOptions.Value.MaximumDenials)
        return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many denied requests",
            extensions: new Dictionary<string, object?> { ["code"] = "denial_rate_limited" });

    var policy = await policies.GetAsync(subject, ct);
    var connection = await catalog.FindAsync(command.ConnectionId, ct);
    if (connection is null || !policy.CanAccessConnection(command.ConnectionId))
    {
        var audit = new SecurityAuditEvent(Guid.NewGuid().ToString("N"), "connection_denied", subject,
            command.ConnectionId, context.TraceIdentifier, "Connection was absent or unauthorized.", DateTimeOffset.UtcNow);
        try { await audits.WriteAsync(audit, ct); }
        catch when (!auditOptions.Value.FailClosed) { }
        catch
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Audit unavailable",
                extensions: new Dictionary<string, object?> { ["code"] = "audit_unavailable" });
        }
        await denials.RecordAsync(subject, audit.OccurredAt, ct);
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Connection unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "connection_denied" });
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Append("X-Accel-Buffering", "no");
    await WriteEvent("status", new { message = "Request accepted." });

    var result = await orchestrator.ExecuteAsync(new NaturalLanguageQueryRequest(command.Question, connection,
        aiOptions.Value.Chat.Profile, aiOptions.Value.Embeddings.Profile, policy.ObjectsFor(connection.Id),
        context.TraceIdentifier, ragOptions.Value.TopK, ragOptions.Value.TokenBudget, subject), ct);
    if (result.Failure?.Code == QueryFailureCode.SqlRejected)
    {
        var audit = new SecurityAuditEvent(Guid.NewGuid().ToString("N"), "sql_rejected", subject,
            connection.Id, context.TraceIdentifier, result.Failure.Message, DateTimeOffset.UtcNow);
        try
        {
            await audits.WriteAsync(audit, ct);
            await denials.RecordAsync(subject, audit.OccurredAt, ct);
        }
        catch when (!auditOptions.Value.FailClosed) { }
        catch
        {
            await WriteEvent("error", new { code = "audit_unavailable", message = "The security audit could not be persisted." });
            return Results.Empty;
        }
    }
    if (!result.IsSuccess)
        await WriteEvent("error", new { code = result.Failure?.Code.ToString(), message = result.Failure?.Message });
    else
    {
        await WriteEvent("answer", new { content = result.Answer });
        await WriteEvent("rows", result.Rows?.Rows ?? []);
        await WriteEvent("complete", new { messageId = context.TraceIdentifier });
    }
    return Results.Empty;

    async Task WriteEvent(string eventName, object? data)
    {
        await context.Response.WriteAsync($"event: {eventName}\n", ct);
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(data)}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
}).RequireAuthorization();

app.Run();

void MapHealth(string path, string tag) => app.MapHealthChecks(path, new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(tag),
    ResponseWriter = static async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString(), checks = report.Entries.Select(x => new { name = x.Key, status = x.Value.Status.ToString() }) });
    }
}).AllowAnonymous();

static void BindOptions(IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<SystemIqOptions>().Bind(configuration.GetSection(SystemIqOptions.SectionName)).ValidateOnStart();
    services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName)).ValidateOnStart();
    services.AddOptions<HistoryOptions>().Bind(configuration.GetSection(HistoryOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<CurationOptions>().Bind(configuration.GetSection(CurationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<DenialStoreOptions>().Bind(configuration.GetSection(DenialStoreOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<ConnectionCatalogOptions>().Bind(configuration.GetSection(ConnectionCatalogOptions.SectionName))
        .Validate(x => x.Connections.All(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.DisplayName) && !string.IsNullOrWhiteSpace(c.Provider)), "Every connection requires id, displayName, and provider.")
        .Validate(x => x.Connections.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == x.Connections.Count, "Connection IDs must be unique.").ValidateOnStart();
    services.AddOptions<AccessPolicyOptions>().Bind(configuration.GetSection(AccessPolicyOptions.SectionName))
        .Validate(x => x.Subjects.All(s => !string.IsNullOrWhiteSpace(s.Subject) && s.Connections.All(c => !string.IsNullOrWhiteSpace(c.Id))),
            "Every access policy subject and connection requires an ID.")
        .Validate(x => x.Subjects.Select(s => s.Subject).Distinct(StringComparer.Ordinal).Count() == x.Subjects.Count,
            "Access policy subjects must be unique.").ValidateOnStart();
    services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName)).ValidateDataAnnotations();
    services.AddOptions<RagOptions>().Bind(configuration.GetSection(RagOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<SqlSafetyOptions>().Bind(configuration.GetSection(SqlSafetyOptions.SectionName)).ValidateDataAnnotations()
        .Validate(x => x.DefaultRowLimit <= x.MaximumRowLimit, "SqlSafety default row limit cannot exceed maximum row limit.").ValidateOnStart();
    services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<AuditOptions>().Bind(configuration.GetSection(AuditOptions.SectionName)).ValidateOnStart();
    services.AddOptions<HealthOptions>().Bind(configuration.GetSection(HealthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<WorkerOptions>().Bind(configuration.GetSection(WorkerOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
    services.AddOptions<TelemetryOptions>().Bind(configuration.GetSection(TelemetryOptions.SectionName)).ValidateOnStart();
}

static string GetSubject(HttpContext context, AuthOptions auth) =>
    context.User.FindFirst(auth.SubjectClaim)?.Value ??
    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

static IResult ConnectionDenied() =>
    Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Connection unavailable",
        extensions: new Dictionary<string, object?> { ["code"] = "connection_denied" });

static FeedbackReviewContract ToFeedbackReviewContract(FeedbackReviewEntry entry) => new(
    entry.Id, entry.ConnectionId, entry.MessageId, entry.Question, entry.MatchedTerms, entry.MatchedTables,
    entry.Reason, entry.Comment, entry.CreatedAt);

public partial class Program;

public sealed record ChatRequestContract(string ConnectionId, string Question);
public sealed record HistoryMessageContract(string Id, string Role, string Content, DateTimeOffset CreatedAt);
public sealed record FeedbackRequestContract(
    string? ConnectionId,
    string? MessageId,
    string? Rating,
    string? Reason,
    string? Comment);
public sealed record FeedbackReviewContract(
    string Id,
    string ConnectionId,
    string MessageId,
    string Question,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<string> MatchedTables,
    string? Reason,
    string? Comment,
    DateTimeOffset CreatedAt);

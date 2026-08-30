using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SystemIQ.Functions.Models;
using SystemIQ.Functions.Security;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Functions;

public sealed class AdminFunctions : FunctionBase
{
    private readonly GlossaryStore _glossary;
    private readonly FeedbackService _feedback;
    private readonly AccuracyReportingService _accuracy;
    private readonly AccessPolicyService _access;
    private readonly SqlQueryService _database;

    public AdminFunctions(
        BearerTokenValidator tokens,
        AccessDenialRateLimiter limiter,
        AuditLogService audit,
        GlossaryStore glossary,
        FeedbackService feedback,
        AccuracyReportingService accuracy,
        AccessPolicyService access,
        SqlQueryService database) : base(tokens, limiter, audit)
    {
        _glossary = glossary;
        _feedback = feedback;
        _accuracy = accuracy;
        _access = access;
        _database = database;
    }

    [Function("GetGlossaryDefaults")]
    public async Task<IActionResult> GetGlossaryDefaults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "curation/glossary/{connectionId}/defaults")] HttpRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var user = auth.User!;
        var userId = BearerTokenValidator.ObjectId(user);
        var roles = user.Claims
            .Where(claim => claim.Type is "roles" or ClaimTypes.Role)
            .Select(claim => claim.Value);
        var policy = _access.GetPolicy(userId, roles);
        if (!policy.Connections.Contains(connectionId))
        {
            try
            {
                await _access.RecordDenialAsync(
                    userId,
                    $"GET curation/glossary/{connectionId}/defaults",
                    connectionId,
                    "Connection is not permitted.",
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return new ObjectResult(new { error = ex.Message })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
            }
            return new ObjectResult(new { error = "Connection is not permitted." }) { StatusCode = 403 };
        }
        return new OkObjectResult(await _database.GetSchemaDefaultsAsync(connectionId, cancellationToken));
    }

    [Function("GetGlossary")]
    public async Task<IActionResult> GetGlossary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "curation/glossary/{connectionId}")] HttpRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        return auth.Error ?? new OkObjectResult(await _glossary.LoadAsync(connectionId, cancellationToken));
    }

    [Function("PutGlossary")]
    public async Task<IActionResult> PutGlossary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "curation/glossary/{connectionId}/{table}")] HttpRequest request,
        string connectionId,
        string table,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var payload = await JsonSerializer.DeserializeAsync<GlossaryEntry>(
            request.Body,
            SystemIQ.Functions.Services.JsonOptions.Default,
            cancellationToken);
        if (payload is null) return new BadRequestObjectResult(new { error = "A glossary entry is required." });
        if (!payload.ConnectionId.Equals(connectionId, StringComparison.OrdinalIgnoreCase) ||
            !payload.Table.Equals(table, StringComparison.OrdinalIgnoreCase))
            return new BadRequestObjectResult(new { error = "Route and entry identifiers must match." });
        await _glossary.UpsertAsync(payload, cancellationToken);
        return new OkObjectResult(payload);
    }

    [Function("GetFeedbackInbox")]
    public async Task<IActionResult> GetFeedbackInbox(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "curation/feedback")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        return new OkObjectResult(await _feedback.PendingAsync(request.Query["connectionId"], cancellationToken));
    }

    [Function("ProcessFeedback")]
    public async Task<IActionResult> ProcessFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "curation/feedback/process")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        return new OkObjectResult(new { processed = await _feedback.ProcessAsync(cancellationToken) });
    }

    [Function("ResolveFeedback")]
    public async Task<IActionResult> ResolveFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "curation/feedback/{id}/resolve")] HttpRequest request,
        string id,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        try
        {
            await _feedback.ResolveAsync(id, cancellationToken);
            return new NoContentResult();
        }
        catch (KeyNotFoundException ex)
        {
            return new NotFoundObjectResult(new { error = ex.Message });
        }
    }

    [Function("AccuracyReport")]
    public async Task<IActionResult> AccuracyReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "curation/accuracy-report")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, true, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        DateTimeOffset? since = null;
        if (int.TryParse(request.Query["days"], out var days) && days > 0)
        {
            since = DateTimeOffset.UtcNow.AddDays(-Math.Min(days, 3650));
        }
        return new OkObjectResult(await _accuracy.CreateAsync(cancellationToken, since));
    }
}

public sealed class FeedbackTimer
{
    private readonly FeedbackService _feedback;
    public FeedbackTimer(FeedbackService feedback) => _feedback = feedback;

    [Function("DailyFeedbackProcessing")]
    public async Task RunAsync([TimerTrigger("0 0 2 * * *")] TimerInfo timer, CancellationToken cancellationToken) =>
        _ = await _feedback.ProcessAsync(cancellationToken);
}

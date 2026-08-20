using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SystemIQ.Functions.Models;
using SystemIQ.Functions.Security;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Functions;

public sealed class UserFunctions : FunctionBase
{
    private readonly AccessPolicyService _access;
    private readonly ConnectionCatalog _connections;
    private readonly BlobChatHistoryStore _history;
    private readonly ChatOrchestrator _chat;
    private readonly FeedbackService _feedback;

    public UserFunctions(
        BearerTokenValidator tokens,
        AccessDenialRateLimiter limiter,
        AuditLogService audit,
        AccessPolicyService access,
        ConnectionCatalog connections,
        BlobChatHistoryStore history,
        ChatOrchestrator chat,
        FeedbackService feedback) : base(tokens, limiter, audit)
    {
        _access = access;
        _connections = connections;
        _history = history;
        _chat = chat;
        _feedback = feedback;
    }

    [Function("Connections")]
    public async Task<IActionResult> Connections(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "connections")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, false, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var user = auth.User!;
        var policy = _access.GetPolicy(BearerTokenValidator.ObjectId(user), Roles(user));
        return new OkObjectResult(_connections.GetPermitted(policy));
    }

    [Function("History")]
    public async Task<IActionResult> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "history/{connectionId}")] HttpRequest request,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, false, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var user = auth.User!;
        var policy = _access.GetPolicy(BearerTokenValidator.ObjectId(user), Roles(user));
        if (!policy.Connections.Contains(connectionId))
        {
            try
            {
                await _access.RecordDenialAsync(
                    BearerTokenValidator.ObjectId(user),
                    $"GET history/{connectionId}",
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
        return new OkObjectResult(await _history.LoadAsync(BearerTokenValidator.ObjectId(user), connectionId, cancellationToken) ?? []);
    }

    [Function("ChatStream")]
    public async Task<IActionResult> ChatStream(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat/stream")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, false, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var payload = await JsonSerializer.DeserializeAsync<ChatRequest>(
            request.Body,
            SystemIQ.Functions.Services.JsonOptions.Default,
            cancellationToken);
        if (payload is null) return new BadRequestObjectResult(new { error = "A request body is required." });
        var user = auth.User!;

        return new SseResult(async (response, ct) =>
        {
            await SseResult.WriteAsync(response, "status", new { message = "Generating query" }, ct);
            try
            {
                var result = await _chat.AskStreamingAsync(
                    BearerTokenValidator.ObjectId(user),
                    Roles(user),
                    payload,
                    (chunk, token) => SseResult.WriteAsync(response, "answer", new { text = chunk }, token),
                    ct);
                await SseResult.WriteAsync(response, "rows", result.Rows, ct);
                await SseResult.WriteAsync(
                    response,
                    "complete",
                    new { result.MessageId, result.MatchedTerms, result.MatchedTables },
                    ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                await SseResult.WriteAsync(response, "error", new { code = "access_denied", message = ex.Message }, ct);
            }
            catch (InvalidOperationException ex)
            {
                await SseResult.WriteAsync(response, "error", new { code = "service_unavailable", message = ex.Message }, ct);
            }
            catch (Exception)
            {
                await SseResult.WriteAsync(
                    response,
                    "error",
                    new { code = "request_failed", message = "The response could not be completed. Try again." },
                    ct);
            }
        });
    }

    [Function("Feedback")]
    public async Task<IActionResult> Feedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "feedback")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(request, false, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        try
        {
            var payload = await JsonSerializer.DeserializeAsync<FeedbackRequest>(
                request.Body,
                SystemIQ.Functions.Services.JsonOptions.Default,
                cancellationToken)
                ?? throw new ArgumentException("A request body is required.");
            await _feedback.SubmitAsync(BearerTokenValidator.ObjectId(auth.User!), payload, cancellationToken);
            return new AcceptedResult();
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return new NotFoundObjectResult(new { error = ex.Message });
        }
    }

    private static IEnumerable<string> Roles(ClaimsPrincipal user) =>
        user.Claims.Where(x => x.Type is "roles" or ClaimTypes.Role).Select(x => x.Value);

}

internal sealed class SseResult : IActionResult
{
    private readonly Func<HttpResponse, CancellationToken, Task> _write;
    public SseResult(Func<HttpResponse, CancellationToken, Task> write) => _write = write;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        context.HttpContext.Response.StatusCode = 200;
        context.HttpContext.Response.ContentType = "text/event-stream";
        context.HttpContext.Response.Headers.CacheControl = "no-cache";
        context.HttpContext.Response.Headers.Append("X-Accel-Buffering", "no");
        await _write(context.HttpContext.Response, context.HttpContext.RequestAborted);
    }

    public static async Task WriteAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync(
            $"data: {JsonSerializer.Serialize(data, SystemIQ.Functions.Services.JsonOptions.Default)}\n\n",
            cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

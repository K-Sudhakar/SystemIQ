using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SystemIQ.Functions.Security;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Functions;

public abstract class FunctionBase
{
    private readonly BearerTokenValidator _tokens;
    private readonly AccessDenialRateLimiter _limiter;
    private readonly AuditLogService _audit;

    protected FunctionBase(
        BearerTokenValidator tokens,
        AccessDenialRateLimiter limiter,
        AuditLogService audit)
    {
        _tokens = tokens;
        _limiter = limiter;
        _audit = audit;
    }

    protected async Task<(ClaimsPrincipal? User, IActionResult? Error)> AuthorizeAsync(
        HttpRequest request,
        bool curator,
        CancellationToken cancellationToken)
    {
        var user = await _tokens.ValidateAsync(request, cancellationToken);
        if (user is null)
        {
            return (null, new UnauthorizedObjectResult(new { error = "A valid bearer token is required." }));
        }

        var userObjectId = BearerTokenValidator.ObjectId(user);
        var rate = await _limiter.GetStatusAsync(userObjectId, cancellationToken);
        if (rate.IsLimited)
        {
            return (null, new ObjectResult(new { error = $"Too many denied requests. Retry after {rate.RetryAfter:O}." })
            {
                StatusCode = StatusCodes.Status429TooManyRequests
            });
        }

        if (curator && !BearerTokenValidator.IsCurator(user))
        {
            try
            {
                await _audit.LogDeniedAccessAsync(
                    userObjectId,
                    $"{request.Method} {request.Path}",
                    "admin",
                    "The glossary editor role is required.",
                    cancellationToken);
                await _limiter.RecordDenialAsync(userObjectId, cancellationToken);
            }
            catch
            {
                return (null, new ObjectResult(new { error = "A security audit system error occurred. Try again shortly." })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                });
            }
            return (null, new ObjectResult(new { error = "The glossary editor role is required." }) { StatusCode = 403 });
        }
        return (user, null);
    }
}

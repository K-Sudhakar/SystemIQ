using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using SystemIQ.Functions.Services;

namespace SystemIQ.Functions.Tests;

public sealed class AccessControlTests
{
    [Fact]
    public void Policy_is_reloaded_and_merges_role_and_user_rules()
    {
        var previous = Environment.GetEnvironmentVariable("RBAC_POLICY_JSON");
        try
        {
            Environment.SetEnvironmentVariable("RBAC_POLICY_JSON", """
                {
                  "analyst": { "connections": ["mp3"], "allowedTables": { "mp3": ["Members"] } },
                  "user-1": { "connections": ["babytrax"], "deniedColumns": { "mp3": ["Ssn"] } }
                }
                """);
            var service = CreatePolicyService();
            var policy = service.GetPolicy("user-1", ["analyst"]);
            Assert.Contains("mp3", policy.Connections);
            Assert.Contains("babytrax", policy.Connections);
            Assert.Contains("Members", policy.AllowedTables["mp3"]);
            Assert.Contains("Ssn", policy.DeniedColumns["mp3"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RBAC_POLICY_JSON", previous);
        }
    }

    [Fact]
    public void Rate_limit_defaults_are_five_denials_in_ten_minutes()
    {
        var count = Environment.GetEnvironmentVariable("RATE_LIMIT_DENIAL_COUNT");
        var window = Environment.GetEnvironmentVariable("RATE_LIMIT_WINDOW_MINUTES");
        try
        {
            Environment.SetEnvironmentVariable("RATE_LIMIT_DENIAL_COUNT", null);
            Environment.SetEnvironmentVariable("RATE_LIMIT_WINDOW_MINUTES", null);
            var limiter = new AccessDenialRateLimiter(new TestCredential(), TimeProvider.System, NullLogger<AccessDenialRateLimiter>.Instance);
            Assert.Equal(5, limiter.Threshold);
            Assert.Equal(TimeSpan.FromMinutes(10), limiter.Window);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RATE_LIMIT_DENIAL_COUNT", count);
            Environment.SetEnvironmentVariable("RATE_LIMIT_WINDOW_MINUTES", window);
        }
    }

    [Fact]
    public async Task Audit_logging_is_fail_closed_when_unconfigured()
    {
        var previous = Environment.GetEnvironmentVariable("AUDIT_LOG_BLOB_CONTAINER_URI");
        try
        {
            Environment.SetEnvironmentVariable("AUDIT_LOG_BLOB_CONTAINER_URI", null);
            var audit = new AuditLogService(new TestCredential(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => audit.LogDeniedAccessAsync("user", "question", "mp3", "denied", default));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUDIT_LOG_BLOB_CONTAINER_URI", previous);
        }
    }

    private static AccessPolicyService CreatePolicyService()
    {
        var credential = new TestCredential();
        var limiter = new AccessDenialRateLimiter(credential, TimeProvider.System, NullLogger<AccessDenialRateLimiter>.Instance);
        return new(new SqlSafetyValidator(), new AuditLogService(credential, TimeProvider.System), limiter,
            NullLogger<AccessPolicyService>.Instance);
    }

    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test", DateTimeOffset.MaxValue);
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccessToken("test", DateTimeOffset.MaxValue));
    }
}

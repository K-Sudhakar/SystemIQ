using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SystemIQ.Functions.Security;

public sealed class BearerTokenValidator
{
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _configuration;
    private readonly string _issuer;
    private readonly string _audience;

    public BearerTokenValidator()
    {
        var tenant = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID") ?? "";
        _audience = Environment.GetEnvironmentVariable("AZURE_AD_API_CLIENT_ID") ?? "";
        _issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{_issuer}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever());
        }
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("AUTH_DISABLED") == "true")
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim("oid", request.Headers["x-test-user"].FirstOrDefault() ?? "local-user"),
                    new Claim("roles", request.Headers["x-test-role"].FirstOrDefault() ?? "")
                },
                "LocalDevelopment");
            return new ClaimsPrincipal(identity);
        }

        if (_configuration is null || string.IsNullOrWhiteSpace(_audience) ||
            !request.Headers.TryGetValue("Authorization", out var header) ||
            !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var config = await _configuration.GetConfigurationAsync(cancellationToken);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            return handler.ValidateToken(header.ToString()["Bearer ".Length..], new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2),
                NameClaimType = "name",
                RoleClaimType = "roles"
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static string ObjectId(ClaimsPrincipal user) =>
        user.FindFirstValue("oid") ?? throw new UnauthorizedAccessException("Token has no object id.");

    public static bool IsCurator(ClaimsPrincipal user) =>
        user.Claims.Any(c =>
            (c.Type == "roles" || c.Type == ClaimTypes.Role) &&
            c.Value.Equals("DataIqGlossaryEditor", StringComparison.Ordinal));
}

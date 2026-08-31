using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SystemIQ.Api.Options;

namespace SystemIQ.Api.Security;

public sealed class DevelopmentHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "DevelopmentHeader";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configured = authOptions.Value.Development;
        if (!Request.Headers.TryGetValue(configured.HeaderName, out var supplied) ||
            supplied.Count != 1 || !string.Equals(supplied[0], configured.Identity, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("A valid fixed development identity is required."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, configured.Identity),
            new(ClaimTypes.Name, configured.DisplayName)
        };
        claims.AddRange(configured.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}

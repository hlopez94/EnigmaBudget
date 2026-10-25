using System.Security.Claims;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EnigmaBudget.Api.Identity;

public sealed class NativeBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NativeSessionService sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NativeBearer";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return AuthenticateResult.NoResult();
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed)
            || !string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parsed.Parameter)) return AuthenticateResult.Fail("Invalid bearer token.");
        var identity = await sessions.AuthenticateAccessAsync(parsed.Parameter, Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("Invalid bearer token.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.Subject),
            new(ClaimTypes.Name, identity.DisplayName ?? identity.Subject),
        };
        if (identity.EmailVerified && !string.IsNullOrWhiteSpace(identity.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, identity.Email));
            claims.Add(new Claim("email_verified", "true"));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Task.CompletedTask;
    }
}

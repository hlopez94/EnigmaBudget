using Microsoft.Extensions.Options;

namespace EnigmaBudget.Api.Identity;

public static class NativeAuthEndpoints
{
    public static void MapNativeAuthEndpoints(this WebApplication app)
    {
        var configuration = app.Services.GetRequiredService<IOptions<NativeAuthConfiguration>>().Value;
        var configuredAudience = configuration.Google.Audiences.Any(x => !string.IsNullOrWhiteSpace(x));
        app.MapGet("/api/auth/native/options", (IHostEnvironment environment, IConfiguration config) =>
            Results.Ok(new NativeAuthOptions(configuredAudience,
                environment.IsDevelopment() && config.GetValue<bool>("Authentication:DevelopmentLoginEnabled")
                && config.GetValue<bool>("Authentication:Native:DevelopmentLoginEnabled"))));

        var publicApi = app.MapGroup("/api/auth/native");
        publicApi.MapPost("/challenge", Challenge).RequireRateLimiting(NativeAuthRateLimits.Challenge);
        publicApi.MapPost("/exchange", Exchange).RequireRateLimiting(NativeAuthRateLimits.Exchange);
        publicApi.MapPost("/refresh", Refresh).RequireRateLimiting(NativeAuthRateLimits.Refresh);
        if (app.Environment.IsDevelopment())
            publicApi.MapPost("/development", Development).RequireRateLimiting(NativeAuthRateLimits.Development);
        publicApi.MapPost("/logout", Logout);

        var authenticated = app.MapGroup("/api/auth/native");
        authenticated.MapGet("/session", async (HttpContext http, WorkspaceProvisioner provisioner) =>
        {
            var personal = await provisioner.EnsurePersonalAsync(http.User, http.RequestAborted);
            return Results.Ok(new NativeSessionInfo(true, personal.OwnerUserId.ToString(), http.User.Identity?.Name));
        }).RequireAuthorization(policy =>
        {
            policy.AddAuthenticationSchemes(NativeBearerHandler.SchemeName);
            policy.RequireAuthenticatedUser();
        });
    }

    private static async Task<IResult> Challenge(HttpContext http, NativeChallengeRequest request, NativeSessionService sessions)
    {
        if (!IsHttps(http)) return UpgradeRequired();
        if (!string.Equals(request.Platform, "android", StringComparison.Ordinal)) return Results.BadRequest();
        return Results.Ok(await sessions.CreateChallengeAsync("android", http.RequestAborted));
    }

    private static async Task<IResult> Exchange(HttpContext http, NativeExchangeRequest request, IOptions<NativeAuthConfiguration> options,
        IGoogleIdTokenValidator validator, NativeSessionService sessions)
    {
        if (!IsHttps(http)) return UpgradeRequired();
        var config = options.Value;
        if (!config.Google.Audiences.Any(x => !string.IsNullOrWhiteSpace(x))) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (request.IdToken is null || request.IdToken.Length > 10_000) return Unauthorized();
        var identity = await validator.ValidateAsync(request.IdToken, http.RequestAborted);
        if (identity is null) return Unauthorized();
        var issued = await sessions.IssueAsync(identity.Subject, identity.DisplayName, identity.EmailVerified ? identity.Email : null, identity.EmailVerified,
            request.DeviceName, request.ChallengeId, identity.Nonce, http.RequestAborted);
        return issued is null ? Unauthorized() : Results.Ok(issued);
    }

    private static async Task<IResult> Refresh(HttpContext http, NativeRefreshRequest request, NativeSessionService sessions)
    {
        if (!IsHttps(http)) return UpgradeRequired();
        var response = await sessions.RefreshAsync(request.RefreshToken, http.RequestAborted);
        return response is null ? Unauthorized() : Results.Ok(response);
    }

    private static async Task<IResult> Logout(HttpContext http, NativeLogoutRequest request, NativeSessionService sessions)
    {
        if (!IsHttps(http)) return UpgradeRequired();
        await sessions.RevokeAsync(request.RefreshToken, http.RequestAborted);
        return Results.NoContent();
    }

    private static async Task<IResult> Development(HttpContext http, NativeDevelopmentLoginRequest request,
        IHostEnvironment environment, IConfiguration configuration, IOptions<DevelopmentIdentityOptions> identities,
        NativeSessionService sessions)
    {
        if (!IsHttps(http)) return UpgradeRequired();
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Authentication:DevelopmentLoginEnabled")
            || !configuration.GetValue<bool>("Authentication:Native:DevelopmentLoginEnabled")) return Results.NotFound();
        var selected = string.IsNullOrWhiteSpace(request.Key) ? null
            : identities.Value.Users.SingleOrDefault(x => string.Equals(x.Key, request.Key, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(request.Key) && selected is null) return Results.NotFound();
        return Results.Ok(await sessions.IssueDevelopmentAsync(selected?.Subject ?? "development-local-user",
            selected?.DisplayName ?? "Usuario de desarrollo", selected?.Email, selected is not null, request.DeviceName, http.RequestAborted));
    }

    private static IResult UpgradeRequired() => Results.StatusCode(StatusCodes.Status426UpgradeRequired);
    private static IResult Unauthorized() => Results.Unauthorized();
    private static bool IsHttps(HttpContext http) => http.Request.IsHttps;
}

public static class NativeAuthRateLimits
{
    public const string Challenge = "native-challenge";
    public const string Exchange = "native-exchange";
    public const string Development = "native-development";
    public const string Refresh = "native-refresh";
}

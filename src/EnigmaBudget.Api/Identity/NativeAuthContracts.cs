namespace EnigmaBudget.Api.Identity;

public sealed record NativeChallengeRequest(string Platform);
public sealed record NativeChallengeResponse(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAt);
public sealed record NativeExchangeRequest(Guid ChallengeId, string IdToken, string? DeviceName);
public sealed record NativeRefreshRequest(string RefreshToken);
public sealed record NativeLogoutRequest(string RefreshToken);
public sealed record NativeDevelopmentLoginRequest(string? Key, string? DeviceName);
public sealed record NativeTokenResponse(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset SessionExpiresAt);
public sealed record NativeSessionInfo(bool Authenticated, string? UserId, string? DisplayName);
public sealed record NativeAuthOptions(bool GoogleConfigured, bool DevelopmentLoginEnabled);
public sealed record ValidatedGoogleIdentity(string Subject, string Nonce, string? DisplayName, string? Email, bool EmailVerified);

public interface IGoogleIdTokenValidator
{
    Task<ValidatedGoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

public sealed class NativeAuthConfiguration
{
    public const string SectionName = "Authentication:Native";
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AbsoluteSessionLifetime { get; set; } = TimeSpan.FromDays(30);
    public bool DevelopmentLoginEnabled { get; set; }
    public NativeGoogleConfiguration Google { get; set; } = new();
}

public sealed class NativeGoogleConfiguration
{
    public string[] Audiences { get; set; } = [];
}

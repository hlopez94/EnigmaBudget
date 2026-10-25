namespace EnigmaBudget.Api.Identity;

public sealed class NativeAuthChallenge
{
    public Guid Id { get; set; }
    public byte[] NonceDigest { get; set; } = [];
    public string Platform { get; set; } = "android";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class NativeSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Subject { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? DeviceName { get; set; }
    public byte[] AccessTokenDigest { get; set; } = [];
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public List<NativeRefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class NativeRefreshToken
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public NativeSession Session { get; set; } = null!;
    public byte[] TokenDigest { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnigmaBudget.Api.Identity;

public sealed class NativeSessionService(AppDbContext db, TimeProvider clock, IOptions<NativeAuthConfiguration> options)
{
    private readonly NativeAuthConfiguration _options = options.Value;

    public async Task<NativeChallengeResponse> CreateChallengeAsync(string platform, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var nonce = NewToken(32);
        var challenge = new NativeAuthChallenge
        {
            Id = Guid.NewGuid(), NonceDigest = Digest(nonce), Platform = platform,
            CreatedAt = now, ExpiresAt = now + _options.ChallengeLifetime,
        };
        db.NativeAuthChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        return new(challenge.Id, nonce, challenge.ExpiresAt);
    }

    public Task<NativeTokenResponse?> IssueAsync(string subject, string? displayName, string? email, bool emailVerified,
        string? deviceName, Guid challengeId, string nonce, CancellationToken cancellationToken) =>
        IssueCoreAsync(subject, displayName, email, emailVerified, updateProfile: true, deviceName, challengeId, nonce, cancellationToken);

    public async Task<NativeTokenResponse> IssueDevelopmentAsync(string subject, string? displayName, string? email, bool emailVerified,
        string? deviceName, CancellationToken cancellationToken) =>
        (await IssueCoreAsync(subject, displayName, emailVerified ? email : null, emailVerified, updateProfile: email is not null,
            deviceName, null, null, cancellationToken))!;

    private async Task<NativeTokenResponse?> IssueCoreAsync(string subject, string? displayName, string? email, bool emailVerified, bool updateProfile,
        string? deviceName, Guid? challengeId, string? nonce, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 512) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (challengeId is Guid id)
        {
            var now = clock.GetUtcNow();
            var exists = await db.NativeAuthChallenges.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken);
            if (!exists) return null;
            var challenge = await db.NativeAuthChallenges.FromSqlInterpolated($"SELECT * FROM \"NativeAuthChallenges\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt <= now || nonce is null
                || !CryptographicOperations.FixedTimeEquals(challenge.NonceDigest, Digest(nonce))) return null;
            challenge.ConsumedAt = now;
        }

        var user = await db.ApplicationUsers.SingleOrDefaultAsync(x => x.Subject == subject, cancellationToken);
        if (user is null)
        {
            user = new ApplicationUser { Subject = subject, DisplayName = displayName, Email = email, EmailVerified = emailVerified, CreatedAt = clock.GetUtcNow() };
            db.ApplicationUsers.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (updateProfile)
        {
            user.Email = email;
            user.EmailVerified = emailVerified;
            if (!string.IsNullOrWhiteSpace(displayName)) user.DisplayName = displayName;
        }

        var created = CreateSession(user.Id, subject, displayName, deviceName);
        db.NativeSessions.Add(created.Session);
        db.NativeRefreshTokens.Add(created.Refresh);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created.Response;
    }

    public async Task<NativeTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 256) return null;
        var digest = Digest(refreshToken);
        var tokenHeader = await db.NativeRefreshTokens.AsNoTracking().Where(x => x.TokenDigest.SequenceEqual(digest))
            .Select(x => new { x.Id, x.SessionId }).SingleOrDefaultAsync(cancellationToken);
        if (tokenHeader is null) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var session = await db.NativeSessions.FromSqlInterpolated($"SELECT * FROM \"NativeSessions\" WHERE \"Id\" = {tokenHeader.SessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null) return null;
        var token = await db.NativeRefreshTokens.FromSqlInterpolated($"SELECT * FROM \"NativeRefreshTokens\" WHERE \"Id\" = {tokenHeader.Id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (token is null) return null;
        var now = clock.GetUtcNow();
        if (token.ConsumedAt is not null)
        {
            if (session.RevokedAt is null)
            {
                session.RevokedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (session.RevokedAt is not null || session.ExpiresAt <= now || token.ExpiresAt <= now) return null;

        var access = NewToken(32);
        var refresh = NewToken(32);
        var refreshEntity = new NativeRefreshToken
        {
            Id = Guid.NewGuid(), SessionId = session.Id, TokenDigest = Digest(refresh), CreatedAt = now, ExpiresAt = session.ExpiresAt,
        };
        token.ConsumedAt = now;
        token.ReplacedById = refreshEntity.Id;
        session.AccessTokenDigest = Digest(access);
        session.AccessExpiresAt = Min(now + _options.AccessLifetime, session.ExpiresAt);
        db.NativeRefreshTokens.Add(refreshEntity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(access, session.AccessExpiresAt, refresh, session.ExpiresAt);
    }

    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 256) return false;
        var digest = Digest(refreshToken);
        var header = await db.NativeRefreshTokens.AsNoTracking().Where(x => x.TokenDigest.SequenceEqual(digest))
            .Select(x => new { x.Id, x.SessionId }).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var session = await db.NativeSessions.FromSqlInterpolated($"SELECT * FROM \"NativeSessions\" WHERE \"Id\" = {header.SessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null) return false;
        if (session.RevokedAt is null) session.RevokedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<NativeAccessIdentity?> AuthenticateAccessAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 256) return null;
        var digest = Digest(accessToken);
        var now = clock.GetUtcNow();
        return await (from session in db.NativeSessions.AsNoTracking()
            join user in db.ApplicationUsers.AsNoTracking() on session.UserId equals user.Id
            where session.AccessTokenDigest.SequenceEqual(digest) && session.RevokedAt == null
                && session.AccessExpiresAt > now && session.ExpiresAt > now
            select new NativeAccessIdentity(session.Subject, session.DisplayName, session.Id, user.Id, user.Email, user.EmailVerified))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private (NativeSession Session, NativeRefreshToken Refresh, NativeTokenResponse Response) CreateSession(Guid userId, string subject, string? displayName, string? deviceName)
    {
        var now = clock.GetUtcNow();
        var sessionExpiry = now + _options.AbsoluteSessionLifetime;
        var access = NewToken(32);
        var refresh = NewToken(32);
        var session = new NativeSession
        {
            Id = Guid.NewGuid(), UserId = userId, Subject = subject, DisplayName = displayName,
            DeviceName = deviceName?.Trim() is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, 120)] : null,
            AccessTokenDigest = Digest(access), AccessExpiresAt = Min(now + _options.AccessLifetime, sessionExpiry),
            CreatedAt = now, ExpiresAt = sessionExpiry,
        };
        var refreshEntity = new NativeRefreshToken { Id = Guid.NewGuid(), Session = session, TokenDigest = Digest(refresh), CreatedAt = now, ExpiresAt = sessionExpiry };
        return (session, refreshEntity, new(access, session.AccessExpiresAt, refresh, sessionExpiry));
    }

    private static string NewToken(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Digest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}

public sealed record NativeAccessIdentity(string Subject, string? DisplayName, Guid SessionId, Guid UserId, string? Email, bool EmailVerified);

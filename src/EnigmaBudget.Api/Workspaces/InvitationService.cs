using System.Security.Cryptography;

namespace EnigmaBudget.Api.Workspaces;

public sealed class InvitationService(TimeProvider clock)
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static (string Token, byte[] Hash) CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return (Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes), SHA256.HashData(bytes));
    }

    public static byte[] HashToken(string token)
    {
        if (token.Length != 43) return [];
        try
        {
            var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token);
            return bytes.Length == 32 ? SHA256.HashData(bytes) : [];
        }
        catch (FormatException) { return []; }
    }

    public DateTimeOffset Now => clock.GetUtcNow();
    public DateTimeOffset ExpiresAt => Now.AddDays(7);
}

using System.Security.Claims;
using System.Text.Json;

namespace EnigmaBudget.Api.Identity;

public static class GoogleIdentityClaims
{
    public static void EnrichFromTrustedProfile(JsonElement profile, ClaimsIdentity identity)
    {
        Set(identity, ClaimTypes.Email, String(profile, "email"));
        Set(identity, ClaimTypes.Name, String(profile, "name"));
        if (profile.TryGetProperty("email_verified", out var verified) && verified.ValueKind is JsonValueKind.True or JsonValueKind.False)
            Set(identity, "email_verified", verified.GetBoolean() ? "true" : "false");
        else
            Remove(identity, "email_verified");
    }

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static void Set(ClaimsIdentity identity, string type, string? value)
    {
        Remove(identity, type);
        if (!string.IsNullOrWhiteSpace(value)) identity.AddClaim(new Claim(type, value));
    }

    private static void Remove(ClaimsIdentity identity, string type)
    {
        foreach (var claim in identity.FindAll(type).ToArray()) identity.RemoveClaim(claim);
    }
}

using System.Security.Claims;

namespace EnigmaBudget.Api.Identity;

public static class CurrentIdentity
{
    public static string Subject(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated identity has no subject.");
}

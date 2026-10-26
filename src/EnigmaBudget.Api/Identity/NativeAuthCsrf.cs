using Microsoft.AspNetCore.Antiforgery;

namespace EnigmaBudget.Api.Identity;

public static class NativeAuthCsrf
{
    public static async Task<bool> ValidateRequestAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (http.User.Identity?.IsAuthenticated == true
            && string.Equals(http.User.Identity.AuthenticationType, NativeBearerHandler.SchemeName, StringComparison.Ordinal))
            return true;
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}

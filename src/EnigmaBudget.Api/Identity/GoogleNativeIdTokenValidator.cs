using Google.Apis.Auth;
using Google.Apis.Util;
using Microsoft.Extensions.Options;

namespace EnigmaBudget.Api.Identity;

public sealed class GoogleNativeIdTokenValidator(IOptions<NativeAuthConfiguration> options, TimeProvider clock) : IGoogleIdTokenValidator
{
    private readonly string[] _audiences = options.Value.Google.Audiences
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    private readonly BoundedGoogleValidation<GoogleJsonWebSignature.Payload> _bounded = new(2, TimeSpan.FromSeconds(5));

    public async Task<ValidatedGoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 10_000 || _audiences.Length == 0)
            return null;
        var payload = await _bounded.ValidateAsync(
            () => GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = _audiences,
                Clock = new TimeProviderClock(clock),
                IssuedAtClockTolerance = TimeSpan.FromSeconds(30),
                ExpirationTimeClockTolerance = TimeSpan.Zero,
            }), cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Nonce)) return null;
        return new ValidatedGoogleIdentity(payload.Subject, payload.Nonce, payload.Name, payload.Email, payload.EmailVerified);
    }

    private sealed class TimeProviderClock(TimeProvider provider) : IClock
    {
        public DateTime Now => provider.GetUtcNow().UtcDateTime;
        public DateTime UtcNow => provider.GetUtcNow().UtcDateTime;
    }
}

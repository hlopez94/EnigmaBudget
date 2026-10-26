using System.Net.Mail;

namespace EnigmaBudget.Api.Identity;

public sealed class DevelopmentIdentityOptions
{
    public const string SectionName = "Authentication:DevelopmentUsers";
    public List<DevelopmentIdentity> Users { get; set; } = [];

    public static bool IsValid(DevelopmentIdentityOptions options) =>
        options.Users.All(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length <= 80
            && !string.IsNullOrWhiteSpace(x.Subject) && x.Subject.Length <= 512
            && !string.IsNullOrWhiteSpace(x.DisplayName) && x.DisplayName.Length <= 256
            && MailAddress.TryCreate(x.Email, out var address) && address.Address.Equals(x.Email, StringComparison.OrdinalIgnoreCase))
        && options.Users.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == options.Users.Count
        && options.Users.Select(x => x.Subject).Distinct(StringComparer.Ordinal).Count() == options.Users.Count;
}

public sealed class DevelopmentIdentity
{
    public string Key { get; set; } = "";
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
}

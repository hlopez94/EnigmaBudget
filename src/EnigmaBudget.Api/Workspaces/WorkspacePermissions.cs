namespace EnigmaBudget.Api.Workspaces;

public static class WorkspacePermissions
{
    public const string Read = "read";
    public const string ReadGroups = "readGroups";
    public const string RecordFinance = "recordFinance";
    public const string ManageResources = "manageResources";
    public const string CorrectHistory = "correctHistory";
    public const string ManageMembers = "manageMembers";
    public const string ManagePrivacy = "managePrivacy";

    public static IReadOnlySet<string> For(string role, bool personalOwner = false) =>
        (role, personalOwner) switch
        {
            ("Owner", true) => new HashSet<string>([Read, ReadGroups, RecordFinance, ManageResources, CorrectHistory, ManageMembers, ManagePrivacy], StringComparer.Ordinal),
            ("Owner", false) or ("Admin", _) => new HashSet<string>([Read, ReadGroups, RecordFinance, ManageResources, CorrectHistory, ManageMembers], StringComparer.Ordinal),
            ("Member", _) => new HashSet<string>([Read, ReadGroups, RecordFinance], StringComparer.Ordinal),
            ("Reader", _) => new HashSet<string>([Read, ReadGroups], StringComparer.Ordinal),
            _ => new HashSet<string>(StringComparer.Ordinal),
        };
}

public sealed record WorkspaceRequestContext(
    ApplicationUser User,
    Data.Workspace Workspace,
    string Role,
    IReadOnlySet<string> Permissions);

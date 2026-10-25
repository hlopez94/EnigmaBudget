namespace EnigmaBudget.Api.Workspaces;

public sealed record WorkspaceDto(
    Guid Id, string Name, string? OwnerDisplayName, int? MembershipVersion, string Kind, string Role, bool IsSharedPersonalRead,
    bool IsArchived, int Version, WorkspacePermissionsDto Permissions);
public sealed record WorkspacePermissionsDto(bool Read, bool ReadGroups, bool RecordFinance, bool ManageResources, bool CorrectHistory, bool ManageMembers, bool ManagePrivacy);
public sealed record MeDto(Guid Id, string DisplayName, string? Email, bool EmailVerified, Guid PersonalWorkspaceId);
public sealed record CreateWorkspaceRequest(Guid Id, string Kind, string Name);
public sealed record UpdateWorkspaceRequest(int Version, string Name, bool IsArchived);
public sealed record ChangeMembershipRequest(int Version, string Role);
public sealed record LeaveWorkspaceRequest(int Version);
public sealed record OwnerTransferRequest(int Version, Guid UserId);
public sealed record WorkspaceMemberDto(Guid UserId, string DisplayName, string Role, bool Active, int Version);
public sealed record CreateInvitationRequest(Guid Id, string Email, string Role);
public sealed record InvitationRequest(string Token);
public sealed record InvitationMutationRequest(int Version);
public sealed record InvitationDto(Guid Id, string Email, string Role, string Status, DateTimeOffset ExpiresAt, int Version, string? ShareUrl);
public sealed record UpdatePersonalSharingRequest(int Version, bool Enabled);
public sealed record PersonalSharingDto(Guid HouseholdId, string HouseholdName, bool Enabled, int Version, Guid[] SharedWithUserIds, bool NeedsAudienceRenewal);

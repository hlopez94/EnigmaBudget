namespace EnigmaBudget.Api.Workspaces;

public sealed class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Subject { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<WorkspaceMembership> Memberships { get; set; } = [];
}

public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Data.Workspace Workspace { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string Role { get; set; }
    public bool Active { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTimeOffset JoinedAt { get; set; }
    public Guid ActivationGeneration { get; set; } = Guid.NewGuid();
}

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required byte[] TokenHash { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Version { get; set; } = 1;
    public Guid? AcceptedByUserId { get; set; }
    public Guid? AcceptedMembershipGeneration { get; set; }
}

public sealed class PersonalReadGrant
{
    public Guid PersonalWorkspaceId { get; set; }
    public Guid HouseholdWorkspaceId { get; set; }
    public bool Enabled { get; set; }
    public int Version { get; set; } = 1;
    public List<PersonalReadGrantAudience> Audience { get; set; } = [];
}

public sealed class PersonalReadGrantAudience
{
    public Guid PersonalWorkspaceId { get; set; }
    public Guid HouseholdWorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid MembershipGeneration { get; set; }
}

public sealed class WorkspaceAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ChangesJson { get; set; } = "{}";
}

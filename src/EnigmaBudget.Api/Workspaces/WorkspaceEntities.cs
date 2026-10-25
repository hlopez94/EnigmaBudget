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

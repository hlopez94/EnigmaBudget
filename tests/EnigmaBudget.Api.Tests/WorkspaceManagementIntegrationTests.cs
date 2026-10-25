using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class WorkspaceManagementIntegrationTests
{
    [Fact]
    public async Task Financial_audit_records_resource_types_without_account_values_or_raw_request_data()
    {
        using var factory = new ApiFactory();
        using var client = factory.IdentityClient("finance-audit-owner");
        await AddCsrfAsync(client);
        var spaces = await client.GetFromJsonAsync<WorkspaceDto[]>("/api/workspaces");
        var personal = Assert.Single(spaces!);
        client.DefaultRequestHeaders.Add("X-Workspace-Id", personal.Id.ToString());

        var result = await client.PostAsJsonAsync("/api/accounts", new
        {
            id = Guid.NewGuid(), name = "Cuenta con valor privado", bankName = "Banco", currency = "ARS",
            openingBalance = "87654.32", openingDate = "2026-10-25",
        });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var audit = await client.GetStringAsync($"/api/workspaces/{personal.Id}/audit");
        Assert.Contains("finance.bankaccount.added", audit, StringComparison.Ordinal);
        Assert.Contains("finance.openingbalancerecord.added", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Cuenta con valor privado", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("87654.32", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("raw", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Household_creation_is_idempotent_and_updates_are_audited_without_subjects_or_secrets()
    {
        using var factory = new ApiFactory();
        using var client = factory.IdentityClient("workspace-owner");
        await AddCsrfAsync(client);
        var request = new { id = Guid.NewGuid(), kind = "Household", name = "Hogar de prueba" };

        var created = await client.PostAsJsonAsync("/api/workspaces", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.NotNull(body);
        Assert.Equal("Owner", body.Role);
        Assert.True(body.Permissions.ManageMembers);
        Assert.False(body.Permissions.ManagePrivacy);

        var retry = await client.PostAsJsonAsync("/api/workspaces", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(body.Id, (await retry.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id);

        var updated = await client.PutAsJsonAsync($"/api/workspaces/{body.Id}", new { version = body.Version, name = "Hogar nuevo", isArchived = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var changed = await updated.Content.ReadFromJsonAsync<WorkspaceDto>();
        Assert.Equal("Hogar nuevo", changed!.Name);
        Assert.Equal(2, changed.Version);

        var audit = await client.GetStringAsync($"/api/workspaces/{body.Id}/audit");
        using var json = JsonDocument.Parse(audit);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Contains("workspace.created", audit, StringComparison.Ordinal);
        Assert.Contains("workspace.updated", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("subject", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        Assert.Equal(1, await db.Workspaces.CountAsync(x => x.Kind == "Household" && x.Id == body.Id));
    }

    [Fact]
    public async Task Owner_can_change_roles_and_transfer_title_but_cannot_remove_the_owner()
    {
        using var factory = new ApiFactory();
        using var owner = factory.IdentityClient("transfer-owner");
        using var member = factory.IdentityClient("transfer-member");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(member);
        var create = await owner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar" });
        var household = (await create.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Guid memberId;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options))
        {
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "transfer-member");
            memberId = user.Id;
            var workspace = await db.Workspaces.SingleAsync(x => x.Id == household.Id);
            db.WorkspaceMemberships.Add(new EnigmaBudget.Api.Workspaces.WorkspaceMembership
            {
                Workspace = workspace, User = user, Role = "Member", JoinedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var changedRole = await owner.PutAsJsonAsync($"/api/workspaces/{household.Id}/members/{memberId}", new { version = 1, role = "Admin" });
        Assert.Equal(HttpStatusCode.NoContent, changedRole.StatusCode);
        var updatedWorkspace = (await owner.GetFromJsonAsync<WorkspaceDto>($"/api/workspaces/{household.Id}"))!;
        var transfer = await owner.PostAsJsonAsync($"/api/workspaces/{household.Id}/owner-transfer", new { version = updatedWorkspace.Version, userId = memberId });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        using var removal = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{household.Id}/members/{memberId}")
        {
            Content = JsonContent.Create(new { version = 2 }),
        };
        var protectedOwner = await owner.SendAsync(removal);
        Assert.Equal(HttpStatusCode.Forbidden, protectedOwner.StatusCode);

        var audit = await owner.GetFromJsonAsync<JsonElement[]>($"/api/workspaces/{household.Id}/audit");
        Assert.NotNull(audit);
        Assert.Contains(audit, entry => entry.GetProperty("action").GetString() == "membership.role_changed");
        Assert.Contains(audit, entry => entry.GetProperty("action").GetString() == "workspace.owner_transferred");

        await using var verified = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        var memberships = await verified.WorkspaceMemberships.Where(x => x.WorkspaceId == household.Id).ToArrayAsync();
        Assert.Equal("Admin", memberships.Single(x => x.UserId != memberId).Role);
        Assert.Equal("Owner", memberships.Single(x => x.UserId == memberId).Role);
        Assert.Equal(memberId, await verified.Workspaces.Where(x => x.Id == household.Id).Select(x => x.OwnerUserId).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_role_changes_serialize_through_the_household_version()
    {
        using var factory = new ApiFactory();
        using var owner = factory.IdentityClient("serial-owner");
        using var firstMember = factory.IdentityClient("serial-member-one");
        using var secondMember = factory.IdentityClient("serial-member-two");
        await AddCsrfAsync(owner);
        await firstMember.GetAsync("/api/auth/session");
        await secondMember.GetAsync("/api/auth/session");
        var response = await owner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar serial" });
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Guid firstId;
        Guid secondId;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options))
        {
            var firstUser = await db.ApplicationUsers.SingleAsync(x => x.Subject == "serial-member-one");
            var secondUser = await db.ApplicationUsers.SingleAsync(x => x.Subject == "serial-member-two");
            firstId = firstUser.Id;
            secondId = secondUser.Id;
            db.WorkspaceMemberships.AddRange(
                new EnigmaBudget.Api.Workspaces.WorkspaceMembership { WorkspaceId = workspace.Id, UserId = firstId, Role = "Member", JoinedAt = DateTimeOffset.UtcNow },
                new EnigmaBudget.Api.Workspaces.WorkspaceMembership { WorkspaceId = workspace.Id, UserId = secondId, Role = "Member", JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var firstChange = owner.PutAsJsonAsync($"/api/workspaces/{workspace.Id}/members/{firstId}", new { version = 1, role = "Admin" });
        var secondChange = owner.PutAsJsonAsync($"/api/workspaces/{workspace.Id}/members/{secondId}", new { version = 1, role = "Admin" });
        var results = await Task.WhenAll(firstChange, secondChange);
        Assert.All(results, result => Assert.Equal(HttpStatusCode.NoContent, result.StatusCode));

        await using var check = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        var version = await check.Workspaces.Where(x => x.Id == workspace.Id).Select(x => x.Version).SingleAsync();
        Assert.Equal(workspace.Version + 2, version);
        Assert.Equal(2, await check.WorkspaceAuditEvents.CountAsync(x => x.WorkspaceId == workspace.Id && x.Action == "membership.role_changed"));
    }

    [Fact]
    public async Task Admin_demoted_while_waiting_for_household_lock_cannot_manage_members()
    {
        using var factory = new ApiFactory();
        using var owner = factory.IdentityClient("lock-demotion-owner");
        using var admin = factory.IdentityClient("lock-demotion-admin");
        using var target = factory.IdentityClient("lock-demotion-target");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(admin);
        await target.GetAsync("/api/auth/session");
        var created = await owner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar democión" });
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Guid adminId;
        Guid targetId;
        await using (var setup = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options))
        {
            var adminUser = await setup.ApplicationUsers.SingleAsync(x => x.Subject == "lock-demotion-admin");
            var targetUser = await setup.ApplicationUsers.SingleAsync(x => x.Subject == "lock-demotion-target");
            adminId = adminUser.Id;
            targetId = targetUser.Id;
            setup.WorkspaceMemberships.AddRange(
                new EnigmaBudget.Api.Workspaces.WorkspaceMembership { WorkspaceId = workspace.Id, UserId = adminId, Role = "Admin", JoinedAt = DateTimeOffset.UtcNow },
                new EnigmaBudget.Api.Workspaces.WorkspaceMembership { WorkspaceId = workspace.Id, UserId = targetId, Role = "Member", JoinedAt = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        await using var blocker = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Workspaces.FromSqlInterpolated($"SELECT * FROM \"Workspaces\" WHERE \"Id\" = {workspace.Id} FOR UPDATE").SingleAsync();

        var managementRequest = admin.PutAsJsonAsync($"/api/workspaces/{workspace.Id}/members/{targetId}", new { version = 1, role = "Reader" });
        await WaitForWorkspaceLockWaitAsync(factory.TestConnection);
        var adminMembership = await blocker.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == adminId);
        adminMembership.Role = "Reader";
        adminMembership.Version++;
        await blocker.SaveChangesAsync();
        await transaction.CommitAsync();

        using var response = await managementRequest;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        Assert.Equal("Member", await verify.WorkspaceMemberships.Where(x => x.WorkspaceId == workspace.Id && x.UserId == targetId).Select(x => x.Role).SingleAsync());
    }

    private static async Task WaitForWorkspaceLockWaitAsync(string connectionString)
    {
        await using var observer = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%FOR UPDATE%' AND pid <> pg_backend_pid()) AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(20, timeout.Token);
    }

    private static async Task AddCsrfAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    }

    private sealed record WorkspaceDto(Guid Id, string Name, string Kind, string Role, int Version, WorkspacePermissionDto Permissions);
    private sealed record WorkspacePermissionDto(bool ManageMembers, bool ManagePrivacy);
}

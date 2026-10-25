using System.Net;
using System.Net.Http.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class WorkspaceIntegrationTests
{
    [Fact]
    public async Task Workspace_list_returns_only_the_authenticated_users_personal_workspace()
    {
        using var factory = new ApiFactory();
        using var first = factory.IdentityClient("workspace-user-one");
        using var second = factory.IdentityClient("workspace-user-two");

        await first.GetAsync("/api/auth/session");
        await second.GetAsync("/api/auth/session");

        var firstList = await first.GetFromJsonAsync<WorkspaceDto[]>("/api/workspaces");
        var secondList = await second.GetFromJsonAsync<WorkspaceDto[]>("/api/workspaces");

        Assert.NotNull(firstList);
        Assert.NotNull(secondList);
        var firstPersonal = Assert.Single(firstList);
        var secondPersonal = Assert.Single(secondList);
        Assert.Equal("Personal", firstPersonal.Kind);
        Assert.Equal("Personal", secondPersonal.Kind);
        Assert.NotEqual(firstPersonal.Id, secondPersonal.Id);

        first.DefaultRequestHeaders.Add("X-Workspace-Id", secondPersonal.Id.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_server_resolved_personal_workspace_without_exposing_subject()
    {
        using var factory = new ApiFactory();
        using var client = factory.IdentityClient("me-user-subject");
        await client.GetAsync("/api/auth/session");

        var response = await client.GetFromJsonAsync<MeResponse>("/api/me");

        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.NotEqual(Guid.Empty, response.PersonalWorkspaceId);
        Assert.Null(response.Email);
        Assert.False(response.EmailVerified);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(response));
        Assert.False(json.RootElement.TryGetProperty("subject", out _));
    }

    [Fact]
    public async Task Reader_can_read_the_selected_workspace_but_cannot_record_finance()
    {
        using var factory = new ApiFactory();
        const string subject = "reader-user";
        using var client = factory.IdentityClient(subject);
        await client.GetAsync("/api/auth/session");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);
        var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == subject);
        var workspace = new Workspace { Id = Guid.NewGuid(), Kind = "Household", Name = "Hogar de prueba", OwnerUserId = user.Id, OwnerUser = user };
        db.Workspaces.Add(workspace);
        var membership = new WorkspaceMembership { Workspace = workspace, User = user, Role = "Reader", JoinedAt = DateTimeOffset.UtcNow };
        var account = new BankAccount { Id = Guid.NewGuid(), Workspace = workspace, WorkspaceId = workspace.Id, Name = "Caja", BankName = "Banco", Currency = "ARS", OpeningBalance = 100m, OpeningDate = new DateOnly(2026, 10, 1) };
        db.WorkspaceMemberships.Add(membership);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        client.DefaultRequestHeaders.Add("X-Workspace-Id", workspace.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts")).StatusCode);
        var write = await client.PostAsJsonAsync("/api/operations", new { });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        membership.Role = "Member";
        await db.SaveChangesAsync();
        var update = await client.PutAsJsonAsync($"/api/accounts/{account.Id}", new { version = 1, name = "Cambio", bankName = "Banco", isArchived = false });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/operations", new { })).StatusCode);
    }

    private sealed record WorkspaceDto(Guid Id, string Name, string Kind, string Role);
    private sealed record MeResponse(Guid Id, string DisplayName, string? Email, bool EmailVerified, Guid PersonalWorkspaceId);
}

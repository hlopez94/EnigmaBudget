using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Finance;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnigmaBudget.Api.Tests;

public sealed class FinanceCatalogIntegrationTests
{
    [Fact]
    public async Task catalog_create_replay_and_changed_raw_payload_are_workspace_scoped()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("catalog-api-create");
        var requestId = Guid.NewGuid();
        var create = new { id = requestId, name = "  Comida  ", kind = "Expense", isDefault = true };

        using var first = await client.PostAsJsonAsync("/api/finance/catalog/categories", create);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal("Comida", firstJson.RootElement.GetProperty("name").GetString());
        var version = firstJson.RootElement.GetProperty("version").GetInt32();

        using var replay = await client.PostAsJsonAsync("/api/finance/catalog/categories", create);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(JsonElement.DeepEquals(firstJson.RootElement, replayJson.RootElement));

        using var update = await client.PutAsJsonAsync($"/api/finance/catalog/categories/{requestId}", new { requestId = Guid.NewGuid(), version, name = "Comidas" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var snapshotReplay = await client.PostAsJsonAsync("/api/finance/catalog/categories", create);
        using var snapshotJson = JsonDocument.Parse(await snapshotReplay.Content.ReadAsStringAsync());
        Assert.Equal("Comida", snapshotJson.RootElement.GetProperty("name").GetString());
        Assert.Equal(version, snapshotJson.RootElement.GetProperty("version").GetInt32());

        using var changed = await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id = requestId, name = "Comida", kind = "Expense" });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var command = await db.CatalogCommands.SingleAsync(x => x.RequestId == requestId);
        Assert.Equal(requestId, command.TargetResourceId);
        Assert.Equal(2, await db.CatalogCommands.CountAsync(x => x.TargetResourceId == requestId));
        Assert.Equal(2, await db.WorkspaceAuditEvents.CountAsync(x => x.ResourceId == requestId.ToString()));

        using var listResponse = await client.GetAsync("/api/finance/catalog");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var category = Assert.Single(list.RootElement.GetProperty("categories").EnumerateArray(), x => x.GetProperty("name").GetString() == "Comidas");
        Assert.Equal(version + 1, category.GetProperty("version").GetInt32());
        Assert.False(category.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task catalog_update_requires_current_version_and_kind_is_immutable()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("catalog-api-version");
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id, name = "Hogar", kind = "Expense" })).StatusCode);

        using var stale = await client.PutAsJsonAsync($"/api/finance/catalog/categories/{id}", new { requestId = Guid.NewGuid(), version = 4, name = "Nueva" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var kindChange = await client.PutAsJsonAsync($"/api/finance/catalog/categories/{id}", new { requestId = Guid.NewGuid(), version = 1, name = "Nueva", kind = "Income" });
        Assert.Equal(HttpStatusCode.BadRequest, kindChange.StatusCode);
    }

    [Fact]
    public async Task default_category_cannot_be_renamed_or_archived_and_archiving_keeps_history()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var client = await factory.AuthenticatedClientAsync("catalog-api-default");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.ApplicationUsers.SingleAsync(x => x.Subject == "catalog-api-default");
        var workspace = await db.Workspaces.SingleAsync(x => x.OwnerUserId == owner.Id && x.Kind == "Personal");
        var defaultCategory = await db.FinanceCategories.SingleAsync(x => x.WorkspaceId == workspace.Id && x.Kind == "Expense" && x.Name == "Sin clasificar");
        using var renameDefault = await client.PutAsJsonAsync($"/api/finance/catalog/categories/{defaultCategory.Id}", new { requestId = Guid.NewGuid(), version = 1, name = "Otro" });
        Assert.Equal(HttpStatusCode.Conflict, renameDefault.StatusCode);
        using var archiveDefault = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/finance/catalog/categories/{defaultCategory.Id}") { Content = JsonContent.Create(new { requestId = Guid.NewGuid(), version = 1 }) });
        Assert.Equal(HttpStatusCode.Conflict, archiveDefault.StatusCode);

        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/finance/catalog/categories", new { id, name = "Histórica", kind = "Expense" })).StatusCode);
        var accountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-10-25" })).StatusCode);
        db.FinanceOperations.Add(new FinanceOperation { Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Kind = "Expense", AccountId = accountId,
            Date = new DateOnly(2026, 10, 25), Description = "Registro de prueba", Category = "Histórica", CategoryId = id, Currency = "ARS", Amount = 10m });
        await db.SaveChangesAsync();
        using var archive = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/finance/catalog/categories/{id}") { Content = JsonContent.Create(new { requestId = Guid.NewGuid(), version = 1 }) });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var archived = await db.FinanceCategories.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.True(archived.IsArchived);
        Assert.Equal(id, await db.FinanceOperations.AsNoTracking().Where(x => x.WorkspaceId == workspace.Id).Select(x => x.CategoryId).SingleAsync());
    }

    [Fact]
    public async Task names_are_exact_and_workspace_local_and_foreign_ids_are_not_found()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var first = await factory.AuthenticatedClientAsync("catalog-api-workspace-a");
        using var second = await factory.AuthenticatedClientAsync("catalog-api-workspace-b");
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/api/finance/catalog/categories", new { id = idA, name = "Comida", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/finance/catalog/categories", new { id = Guid.NewGuid(), name = "Comida", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/api/finance/catalog/categories", new { id = Guid.NewGuid(), name = "comida", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.PostAsJsonAsync("/api/finance/catalog/categories", new { id = idB, name = "Comida", kind = "Expense" })).StatusCode);
        first.DefaultRequestHeaders.Remove("X-Workspace-Id");
        first.DefaultRequestHeaders.Add("X-Workspace-Id", (await WorkspaceIdAsync(factory, "catalog-api-workspace-a")).ToString());
        using var foreign = await first.PutAsJsonAsync($"/api/finance/catalog/categories/{idB}", new { requestId = Guid.NewGuid(), version = 1, name = "Ajena" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task catalog_readers_can_read_but_record_finance_members_cannot_manage_catalogs()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var owner = await factory.AuthenticatedClientAsync("catalog-api-owner");
        using var member = await factory.AuthenticatedClientAsync("catalog-api-member");
        using var reader = await factory.AuthenticatedClientAsync("catalog-api-reader");
        var workspaceId = await WorkspaceIdAsync(factory, "catalog-api-owner");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var memberUser = await db.ApplicationUsers.SingleAsync(x => x.Subject == "catalog-api-member");
            var readerUser = await db.ApplicationUsers.SingleAsync(x => x.Subject == "catalog-api-reader");
            db.WorkspaceMemberships.AddRange(
                new WorkspaceMembership { WorkspaceId = workspaceId, UserId = memberUser.Id, Role = "Member", JoinedAt = DateTimeOffset.UtcNow },
                new WorkspaceMembership { WorkspaceId = workspaceId, UserId = readerUser.Id, Role = "Reader", JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        foreach (var client in new[] { member, reader }) client.DefaultRequestHeaders.Add("X-Workspace-Id", workspaceId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/finance/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/finance/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/finance/catalog/categories", new { id = Guid.NewGuid(), name = "No", kind = "Expense" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/finance/catalog/categories", new { id = Guid.NewGuid(), name = "No", kind = "Expense" })).StatusCode);
    }

    [Fact]
    public async Task catalog_replay_rechecks_current_permission_after_workspace_lock()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var owner = await factory.AuthenticatedClientAsync("catalog-api-replay-owner");
        var workspaceId = await WorkspaceIdAsync(factory, "catalog-api-replay-owner");
        var create = new { id = Guid.NewGuid(), name = "Alimentos", kind = "Expense" };
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/finance/catalog/categories", create)).StatusCode);

        factory.WorkspaceLockBarrier.Arm();
        var replayTask = owner.PostAsJsonAsync("/api/finance/catalog/categories", create);
        await factory.WorkspaceLockBarrier.WaitUntilBlockedAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "catalog-api-replay-owner");
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspaceId && x.UserId == user.Id);
            membership.Role = "Reader";
            await db.SaveChangesAsync();
        }
        factory.WorkspaceLockBarrier.Release();
        using var replay = await replayTask;
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.CatalogCommands.CountAsync(x => x.WorkspaceId == workspaceId));
    }

    [Fact]
    public async Task shared_personal_read_grant_allows_catalog_read_only()
    {
        using var factory = new ApiFactory();
        await MigrateAsync(factory);
        using var owner = await factory.AuthenticatedClientAsync("catalog-api-shared-owner");
        using var householdOwner = await factory.AuthenticatedClientAsync("catalog-api-shared-reader");
        var personalId = await WorkspaceIdAsync(factory, "catalog-api-shared-owner");
        using var householdResponse = await householdOwner.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar lector" });
        Assert.Equal(HttpStatusCode.Created, householdResponse.StatusCode);
        using var householdJson = JsonDocument.Parse(await householdResponse.Content.ReadAsStringAsync());
        var householdId = householdJson.RootElement.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audienceUser = await db.ApplicationUsers.SingleAsync(x => x.Subject == "catalog-api-shared-reader");
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == householdId && x.UserId == audienceUser.Id);
            db.PersonalReadGrants.Add(new PersonalReadGrant { PersonalWorkspaceId = personalId, HouseholdWorkspaceId = householdId, Enabled = true, Version = 1 });
            db.PersonalReadGrantAudience.Add(new PersonalReadGrantAudience { PersonalWorkspaceId = personalId, HouseholdWorkspaceId = householdId, UserId = audienceUser.Id, MembershipGeneration = membership.ActivationGeneration });
            await db.SaveChangesAsync();
        }

        householdOwner.DefaultRequestHeaders.Add("X-Workspace-Id", personalId.ToString());
        using var read = await householdOwner.GetAsync("/api/finance/catalog");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var blocked = await householdOwner.PostAsJsonAsync("/api/finance/catalog/categories", new { id = Guid.NewGuid(), name = "Privacidad", kind = "Expense" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/finance/catalog", WorkspacePermissions.Read)]
    [InlineData("POST", "/api/finance/catalog/categories", WorkspacePermissions.ManageResources)]
    [InlineData("PUT", "/api/finance/catalog/categories/123", WorkspacePermissions.ManageResources)]
    [InlineData("DELETE", "/api/finance/catalog/merchants/123", WorkspacePermissions.ManageResources)]
    public void catalog_route_permissions_are_explicit(string method, string path, string expected)
    {
        var classifier = typeof(FinanceWorkspaceFilter).GetMethod("RequiredPermission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, classifier.Invoke(null, [method, new Microsoft.AspNetCore.Http.PathString(path)]));
    }

    private static async Task MigrateAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    private static async Task<Guid> WorkspaceIdAsync(ApiFactory factory, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == subject);
        return await db.Workspaces.Where(x => x.OwnerUserId == user.Id && x.Kind == "Personal").Select(x => x.Id).SingleAsync();
    }
}

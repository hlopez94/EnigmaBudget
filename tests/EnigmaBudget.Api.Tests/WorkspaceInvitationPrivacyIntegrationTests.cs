using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using EnigmaBudget.Api.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Api.Tests;

public sealed class WorkspaceInvitationPrivacyIntegrationTests
{
    [Fact]
    public async Task Older_admin_invitation_cannot_reverse_a_later_reader_demotion_or_change_household_finances()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var owner = VerifiedClient(factory, "authority-owner", "owner@example.test");
        using var member = VerifiedClient(factory, "authority-member", "member@example.test");
        await owner.GetAsync("/api/auth/session");
        await member.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(member);
        var household = await CreateHouseholdAsync(owner);
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", household.Id.ToString());
        var originalAccountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/accounts", new
        {
            id = originalAccountId, name = "Caja del hogar", bankName = "Banco", currency = "ARS",
            openingBalance = "1234.50", openingDate = "2026-10-25",
        })).StatusCode);

        var oldAdminInvite = await CreateInvitationAsync(owner, household.Id, "member@example.test", "Admin");
        var joiningInvite = await CreateInvitationAsync(owner, household.Id, "member@example.test", "Member");
        var joined = await PostTokenAsync(member, TokenFrom(joiningInvite.ShareUrl));
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var memberId = await UserIdAsync(factory, "authority-member");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/workspaces/{household.Id}/members/{memberId}", new { version = 1, role = "Reader" })).StatusCode);

        var oldAccept = await PostTokenAsync(member, TokenFrom(oldAdminInvite.ShareUrl));
        Assert.Equal(HttpStatusCode.OK, oldAccept.StatusCode);
        using (var accepted = JsonDocument.Parse(await oldAccept.Content.ReadAsStringAsync()))
            Assert.Equal("Reader", accepted.RootElement.GetProperty("role").GetString());

        await using (var inspect = Db(factory))
        {
            var current = await inspect.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == household.Id && x.UserId == memberId);
            Assert.True(current.Active);
            Assert.Equal("Reader", current.Role);
            Assert.Equal(2, current.Version);
            Assert.Equal("Accepted", await inspect.WorkspaceInvitations.Where(x => x.Id == oldAdminInvite.Id).Select(x => x.Status).SingleAsync());
            Assert.Equal(1, await inspect.Accounts.CountAsync(x => x.WorkspaceId == household.Id));
            Assert.Equal(1234.50m, await inspect.Accounts.Where(x => x.Id == originalAccountId).Select(x => x.OpeningBalance).SingleAsync());
        }
        member.DefaultRequestHeaders.Add("X-Workspace-Id", household.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/accounts")).StatusCode);
        var attemptedWrite = await member.PostAsJsonAsync("/api/accounts", new
        {
            id = Guid.NewGuid(), name = "Cuenta no autorizada", bankName = "Banco", currency = "ARS",
            openingBalance = "1.00", openingDate = "2026-10-25",
        });
        Assert.Equal(HttpStatusCode.Forbidden, attemptedWrite.StatusCode);
        await using var afterWrite = Db(factory);
        Assert.Equal(1, await afterWrite.Accounts.CountAsync(x => x.WorkspaceId == household.Id));
        Assert.Equal(1234.50m, await afterWrite.Accounts.Where(x => x.Id == originalAccountId).Select(x => x.OpeningBalance).SingleAsync());
    }

    [Fact]
    public async Task Invitation_issued_before_removal_cannot_reactivate_member_but_fresh_invite_rejoins_without_personal_consent()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var owner = VerifiedClient(factory, "removal-authority-owner", "owner@example.test");
        using var member = VerifiedClient(factory, "removal-authority-member", "member@example.test");
        await owner.GetAsync("/api/auth/session");
        await member.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(member);
        var memberPersonalId = (await member.GetFromJsonAsync<JsonElement[]>("/api/workspaces"))!
            .Single(x => x.GetProperty("kind").GetString() == "Personal").GetProperty("id").GetGuid();
        member.DefaultRequestHeaders.Add("X-Workspace-Id", memberPersonalId.ToString());
        var memberPersonalAccountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/accounts", new
        {
            id = memberPersonalAccountId, name = "Cuenta privada", bankName = "Banco", currency = "ARS",
            openingBalance = "321.09", openingDate = "2026-10-25",
        })).StatusCode);
        member.DefaultRequestHeaders.Remove("X-Workspace-Id");
        var household = await CreateHouseholdAsync(owner);
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", household.Id.ToString());
        var originalAccountId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/accounts", new
        {
            id = originalAccountId, name = "Caja del hogar", bankName = "Banco", currency = "ARS",
            openingBalance = "987.65", openingDate = "2026-10-25",
        })).StatusCode);

        var firstInvite = await CreateInvitationAsync(owner, household.Id, "member@example.test", "Member");
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(member, TokenFrom(firstInvite.ShareUrl))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 1, enabled = true })).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", memberPersonalId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/accounts")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", household.Id.ToString());
        var pendingBeforeRemoval = await CreateInvitationAsync(owner, household.Id, "member@example.test", "Admin");
        var memberId = await UserIdAsync(factory, "removal-authority-member");
        using (var remove = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{household.Id}/members/{memberId}")
        {
            Content = JsonContent.Create(new { version = 1 }),
        })
        {
            remove.Headers.Add("X-CSRF-TOKEN", owner.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(remove)).StatusCode);
        }

        var staleAccept = await PostTokenAsync(member, TokenFrom(pendingBeforeRemoval.ShareUrl));
        Assert.Equal(HttpStatusCode.NotFound, staleAccept.StatusCode);
        member.DefaultRequestHeaders.Add("X-Workspace-Id", household.Id.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync("/api/accounts")).StatusCode);
        await using (var verifyRemoval = Db(factory))
        {
            var current = await verifyRemoval.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == household.Id && x.UserId == memberId);
            Assert.False(current.Active);
            Assert.Equal("Member", current.Role);
            Assert.Equal(2, current.Version);
            Assert.Equal(1, await verifyRemoval.Accounts.CountAsync(x => x.WorkspaceId == household.Id));
            Assert.Equal(987.65m, await verifyRemoval.Accounts.Where(x => x.Id == originalAccountId).Select(x => x.OpeningBalance).SingleAsync());
        }

        var freshInvite = await CreateInvitationAsync(owner, household.Id, "member@example.test", "Reader");
        var freshAccept = await PostTokenAsync(member, TokenFrom(freshInvite.ShareUrl));
        Assert.Equal(HttpStatusCode.OK, freshAccept.StatusCode);
        using (var accepted = JsonDocument.Parse(await freshAccept.Content.ReadAsStringAsync()))
            Assert.Equal("Reader", accepted.RootElement.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/accounts")).StatusCode);
        member.DefaultRequestHeaders.Remove("X-Workspace-Id");
        member.DefaultRequestHeaders.Add("X-Workspace-Id", memberPersonalId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/accounts")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", memberPersonalId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/accounts")).StatusCode);
        var privacy = await member.GetFromJsonAsync<JsonElement[]>("/api/personal-sharing");
        var grant = Assert.Single(privacy!, x => x.GetProperty("householdId").GetGuid() == household.Id);
        Assert.False(grant.GetProperty("enabled").GetBoolean());
        Assert.Empty(grant.GetProperty("sharedWithUserIds").EnumerateArray());
        await using var verifyRejoin = Db(factory);
        var rejoined = await verifyRejoin.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == household.Id && x.UserId == memberId);
        Assert.True(rejoined.Active);
        Assert.Equal("Reader", rejoined.Role);
        Assert.Equal(3, rejoined.Version);
        Assert.Equal(1, await verifyRejoin.Accounts.CountAsync(x => x.WorkspaceId == household.Id));
        Assert.Equal(987.65m, await verifyRejoin.Accounts.Where(x => x.Id == originalAccountId).Select(x => x.OpeningBalance).SingleAsync());
        Assert.Empty(await verifyRejoin.PersonalReadGrantAudience.Where(x => x.PersonalWorkspaceId == memberPersonalId && x.HouseholdWorkspaceId == household.Id).ToArrayAsync());
        Assert.Equal(321.09m, await verifyRejoin.Accounts.Where(x => x.Id == memberPersonalAccountId).Select(x => x.OpeningBalance).SingleAsync());
    }

    [Fact]
    public async Task Pending_invitation_bound_to_removed_subject_cannot_be_accepted_by_recycled_verified_email()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(DateTimeOffset.Parse("2026-10-25T12:00:00Z")));
        using var owner = VerifiedClient(factory, "recycled-email-owner", "owner@example.test");
        using var removed = VerifiedClient(factory, "recycled-email-removed", "same@example.test");
        using var recycled = VerifiedClient(factory, "recycled-email-new", "same@example.test");
        await owner.GetAsync("/api/auth/session");
        await removed.GetAsync("/api/auth/session");
        await recycled.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(removed);
        await AddCsrfAsync(recycled);

        var household = await CreateHouseholdAsync(owner);
        var householdId = household.Id;
        var member = await CreateInvitationAsync(owner, householdId, "same@example.test", "Member");
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(removed, TokenFrom(member.ShareUrl))).StatusCode);

        var pendingAdmin = await CreateInvitationAsync(owner, householdId, "same@example.test", "Admin");
        var removedUserId = await UserIdAsync(factory, "recycled-email-removed");
        using (var remove = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{householdId}/members/{removedUserId}")
        {
            Content = JsonContent.Create(new { version = 1 }),
        })
        {
            remove.Headers.Add("X-CSRF-TOKEN", owner.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(remove)).StatusCode);
        }

        var rejected = await PostTokenAsync(recycled, TokenFrom(pendingAdmin.ShareUrl));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        await using var db = Db(factory);
        Assert.False(await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == householdId && x.User.Subject == "recycled-email-new"));
        Assert.Equal("Pending", await db.WorkspaceInvitations.Where(x => x.Id == pendingAdmin.Id).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Invitation_requires_csrf_and_current_verified_recipient_even_when_database_profile_is_verified()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "invite-owner", "owner@example.test");
        using var invitee = VerifiedClient(factory, "invitee", "reader@example.test");
        using var staleCookie = await factory.AuthenticatedClientAsync();
        await owner.GetAsync("/api/auth/session");
        await invitee.GetAsync("/api/auth/session");
        await staleCookie.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        var household = await CreateHouseholdAsync(owner);

        var csrfToken = owner.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var noCsrf = await owner.PostAsJsonAsync($"/api/workspaces/{household.Id}/invitations", new { id = Guid.NewGuid(), email = "reader@example.test", role = "Reader" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrfToken);
        var created = await CreateInvitationAsync(owner, household.Id, "reader@example.test");
        var token = TokenFrom(created.ShareUrl);

        await using (var db = Db(factory))
        {
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "development-local-user");
            user.Email = "reader@example.test";
            user.EmailVerified = true;
            await db.SaveChangesAsync();
        }
        await AddCsrfAsync(staleCookie);
        var oldCookieAccept = await PostTokenAsync(staleCookie, token);
        Assert.Equal(HttpStatusCode.Forbidden, oldCookieAccept.StatusCode);

        await using (var db = Db(factory))
        {
            var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "development-local-user");
            user.Email = "reader@example.test";
            user.EmailVerified = true;
            await db.SaveChangesAsync();
        }
        using var unverified = factory.IdentityClient("invitee");
        unverified.DefaultRequestHeaders.Add("X-Test-Email", "reader@example.test");
        unverified.DefaultRequestHeaders.Add("X-Test-Email-Verified", "false");
        await unverified.GetAsync("/api/auth/session");
        await AddCsrfAsync(unverified);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostTokenAsync(unverified, token)).StatusCode);

        invitee.DefaultRequestHeaders.Remove("X-Test-Email");
        invitee.DefaultRequestHeaders.Add("X-Test-Email", "other@example.test");
        await AddCsrfAsync(invitee);
        var wrongRecipientAccept = await PostTokenAsync(invitee, token);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRecipientAccept.StatusCode);
    }

    [Fact]
    public async Task Accepted_member_gets_only_currently_consented_personal_read_and_cannot_read_groups_or_write()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "privacy-owner", "owner@example.test");
        using var reader = VerifiedClient(factory, "privacy-reader", "reader@example.test");
        await owner.GetAsync("/api/auth/session");
        await reader.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(reader);
        var ownerSpaces = await owner.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        var personal = Assert.Single(ownerSpaces!);
        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", personal.GetProperty("id").GetString());
        var groupId = Guid.NewGuid();
        var selfParticipant = Guid.NewGuid();
        var otherParticipant = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/groups", new
        {
            id = groupId,
            name = "Grupo privado",
            participants = new[] { new { id = selfParticipant, name = "Titular", isSelf = true }, new { id = otherParticipant, name = "Invitado privado", isSelf = false } },
        })).StatusCode);
        var cardId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/cards", new
        {
            id = cardId, name = "Tarjeta propia", bankName = "Banco", closingDay = 10, dueDay = 20,
            statementDebtArs = "0", statementDebtUsd = "0", asOfDate = "2026-10-25",
        })).StatusCode);
        var purchaseId = Guid.NewGuid();
        var purchase = await owner.PostAsJsonAsync("/api/purchases", new
        {
            id = purchaseId, cardId, date = "2026-10-25", description = "Compra propia", category = "Hogar",
            currency = "ARS", totalAmount = "100.00", installmentCount = 1, firstPeriod = "2026-10", groupId,
        });
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        var household = await CreateHouseholdAsync(owner);
        var invitation = await CreateInvitationAsync(owner, household.Id, "reader@example.test");
        var token = TokenFrom(invitation.ShareUrl);
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(reader, token)).StatusCode);

        var readerSpacesBeforeConsent = await reader.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        Assert.DoesNotContain(readerSpacesBeforeConsent!, x => x.GetProperty("id").GetGuid() == personal.GetProperty("id").GetGuid());

        var sharing = await owner.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 1, enabled = true });
        Assert.Equal(HttpStatusCode.OK, sharing.StatusCode);
        await using (var profiles = Db(factory))
        {
            (await profiles.ApplicationUsers.SingleAsync(x => x.Subject == "privacy-owner")).DisplayName = "Ana Titular";
            (await profiles.ApplicationUsers.SingleAsync(x => x.Subject == "privacy-reader")).DisplayName = "Bruno Lector";
            await profiles.SaveChangesAsync();
        }

        var readerSpaces = await reader.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        Assert.NotNull(readerSpaces);
        var shared = Assert.Single(readerSpaces!, x => x.GetProperty("id").GetGuid() == personal.GetProperty("id").GetGuid());
        Assert.Equal("Reader", shared.GetProperty("role").GetString());
        Assert.True(shared.GetProperty("isSharedPersonalRead").GetBoolean());
        Assert.Equal("Ana Titular", shared.GetProperty("ownerDisplayName").GetString());
        Assert.DoesNotContain("email", shared.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subject", shared.GetRawText(), StringComparison.OrdinalIgnoreCase);

        reader.DefaultRequestHeaders.Add("X-Workspace-Id", personal.GetProperty("id").GetString());
        using var overview = await reader.GetAsync("/api/finance");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var overviewJson = await overview.Content.ReadAsStringAsync();
        using var overviewDoc = JsonDocument.Parse(overviewJson);
        Assert.False(overviewDoc.RootElement.TryGetProperty("groups", out _));
        Assert.False(overviewDoc.RootElement.TryGetProperty("receivables", out _));
        Assert.DoesNotContain("Grupo privado", overviewJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Invitado privado", overviewJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/cards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/cards/{cardId}/purchases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/purchases/{purchaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/groups/{groupId}/receivables?period=2026-10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/operations", new { })).StatusCode);
        var privacy = await owner.GetFromJsonAsync<JsonElement[]>($"/api/personal-sharing");
        var grant = Assert.Single(privacy!, x => x.GetProperty("householdId").GetGuid() == household.Id);
        Assert.Contains(readerSpaces!, x => x.GetProperty("id").GetGuid() == personal.GetProperty("id").GetGuid());
        var readerId = await UserIdAsync(factory, "privacy-reader");
        Assert.Contains(grant.GetProperty("sharedWithUserIds").EnumerateArray(), id => id.GetGuid() == readerId);
    }

    [Fact]
    public async Task Rejoining_household_does_not_restore_personal_read_until_owner_renews_audience()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "rejoin-owner", "owner@example.test");
        using var reader = VerifiedClient(factory, "rejoin-reader", "reader@example.test");
        await owner.GetAsync("/api/auth/session");
        await reader.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(reader);
        var personalRows = await owner.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        var personal = Assert.Single(personalRows!);
        var household = await CreateHouseholdAsync(owner);
        var invite = await CreateInvitationAsync(owner, household.Id, "reader@example.test");
        var token = TokenFrom(invite.ShareUrl);
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(reader, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(reader, token)).StatusCode);
        var readerSpaces = await reader.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        var readerPersonal = Assert.Single(readerSpaces!, x => x.GetProperty("kind").GetString() == "Personal");
        var readerPersonalId = readerPersonal.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await reader.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 1, enabled = true })).StatusCode);
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", readerPersonalId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/accounts")).StatusCode);
        var readerId = await UserIdAsync(factory, "rejoin-reader");
        reader.DefaultRequestHeaders.Add("X-Workspace-Id", personal.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 1, enabled = true })).StatusCode);
        var initialAudience = await owner.GetFromJsonAsync<JsonElement[]>($"/api/personal-sharing");
        Assert.False(Assert.Single(initialAudience!, x => x.GetProperty("householdId").GetGuid() == household.Id).GetProperty("needsAudienceRenewal").GetBoolean());
        var role = await owner.PutAsJsonAsync($"/api/workspaces/{household.Id}/members/{readerId}", new { version = 1, role = "Admin" });
        Assert.Equal(HttpStatusCode.NoContent, role.StatusCode);
        var afterRoleChange = await owner.GetFromJsonAsync<JsonElement[]>($"/api/personal-sharing");
        Assert.False(Assert.Single(afterRoleChange!, x => x.GetProperty("householdId").GetGuid() == household.Id).GetProperty("needsAudienceRenewal").GetBoolean());

        using (var leave = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{household.Id}/leave") { Content = JsonContent.Create(new { version = 2 }) })
        {
            leave.Headers.Add("X-CSRF-TOKEN", reader.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(HttpStatusCode.NoContent, (await reader.SendAsync(leave)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostTokenAsync(reader, token)).StatusCode);
        var secondInvite = await CreateInvitationAsync(owner, household.Id, "reader@example.test");
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(reader, TokenFrom(secondInvite.ShareUrl))).StatusCode);

        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", personal.GetProperty("id").GetString());
        var renewedInfo = await owner.GetFromJsonAsync<JsonElement[]>($"/api/personal-sharing");
        Assert.True(Assert.Single(renewedInfo!, x => x.GetProperty("householdId").GetGuid() == household.Id).GetProperty("needsAudienceRenewal").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/api/accounts")).StatusCode);
        var renew = await owner.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 2, enabled = true });
        Assert.Equal(HttpStatusCode.OK, renew.StatusCode);
        await using (var inspect = Db(factory))
        {
            var membershipGeneration = await inspect.WorkspaceMemberships.Where(x => x.WorkspaceId == household.Id && x.UserId == readerId).Select(x => x.ActivationGeneration).SingleAsync();
            var grantGeneration = await inspect.PersonalReadGrantAudience.Where(x => x.HouseholdWorkspaceId == household.Id && x.UserId == readerId).Select(x => x.MembershipGeneration).SingleAsync();
            Assert.Equal(membershipGeneration, grantGeneration);
            Assert.True(await inspect.PersonalReadGrants.AnyAsync(x => x.PersonalWorkspaceId == personal.GetProperty("id").GetGuid() && x.HouseholdWorkspaceId == household.Id && x.Enabled));
            Assert.True(await inspect.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == household.Id && x.UserId == readerId && x.Active));
        }
        using var readable = await reader.GetAsync("/api/accounts");
        Assert.True(readable.StatusCode == HttpStatusCode.OK, await readable.Content.ReadAsStringAsync());
        owner.DefaultRequestHeaders.Remove("X-Workspace-Id");
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", readerPersonalId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_pending_invitations_and_acceptances_never_exceed_four_seats()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "seat-owner", "owner@example.test");
        await owner.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        var household = await CreateHouseholdAsync(owner);
        var invitees = new List<(HttpClient Client, string Token)>();
        for (var i = 1; i <= 3; i++)
        {
            var email = $"seat-{i}@example.test";
            var invite = await CreateInvitationAsync(owner, household.Id, email);
            var client = VerifiedClient(factory, $"seat-user-{i}", email);
            await client.GetAsync("/api/auth/session");
            await AddCsrfAsync(client);
            invitees.Add((client, TokenFrom(invite.ShareUrl)));
        }
        var accepted = await Task.WhenAll(invitees.Select(x => PostTokenAsync(x.Client, x.Token)));
        Assert.All(accepted, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await using var db = Db(factory);
        Assert.Equal(4, await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == household.Id && x.Active));
        foreach (var response in accepted) response.Dispose();
        foreach (var invitee in invitees) invitee.Client.Dispose();
    }

    [Fact]
    public async Task Member_can_leave_with_own_membership_version_without_roster_read_permission()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "leave-owner", "leave-owner@example.test");
        using var member = VerifiedClient(factory, "leave-member", "leave-member@example.test");
        await owner.GetAsync("/api/auth/session");
        await member.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(member);
        var household = await CreateHouseholdAsync(owner);
        var invitation = await CreateInvitationAsync(owner, household.Id, "leave-member@example.test");
        var accept = await PostTokenAsync(member, TokenFrom(invitation.ShareUrl));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        await using (var db = Db(factory))
        {
            var workspace = await db.Workspaces.SingleAsync(x => x.Id == household.Id);
            workspace.Version = 7;
            await db.SaveChangesAsync();
        }
        using var roster = await member.GetAsync($"/api/workspaces/{household.Id}/members");
        Assert.Equal(HttpStatusCode.Forbidden, roster.StatusCode);
        var spaces = await member.GetFromJsonAsync<JsonElement[]>("/api/workspaces");
        var selected = Assert.Single(spaces!, x => x.GetProperty("id").GetGuid() == household.Id);
        Assert.Equal(7, selected.GetProperty("version").GetInt32());
        Assert.Equal(1, selected.GetProperty("membershipVersion").GetInt32());

        using var leave = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{household.Id}/leave")
        {
            Content = JsonContent.Create(new { version = selected.GetProperty("membershipVersion").GetInt32() }),
        };
        leave.Headers.Add("X-CSRF-TOKEN", member.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
        using var response = await member.SendAsync(leave);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Competing_invitation_creates_reserve_only_the_remaining_three_household_seats()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "pending-seat-owner", "owner@example.test");
        await owner.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        var household = await CreateHouseholdAsync(owner);
        var requests = Enumerable.Range(1, 4).Select(i => owner.PostAsJsonAsync($"/api/workspaces/{household.Id}/invitations", new { id = Guid.NewGuid(), email = $"pending-{i}@example.test", role = "Reader" }));
        var results = await Task.WhenAll(requests);
        Assert.Equal(3, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = Db(factory);
        var active = await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == household.Id && x.Active);
        var pending = await db.WorkspaceInvitations.CountAsync(x => x.WorkspaceId == household.Id && x.Status == "Pending");
        Assert.Equal(4, active + pending);
        foreach (var result in results) result.Dispose();
    }

    [Fact]
    public async Task Invitation_retry_never_recovers_token_and_revoked_or_expired_tokens_are_rejected()
    {
        using var factory = new ApiFactory();
        using var owner = VerifiedClient(factory, "recovery-owner", "owner@example.test");
        using var reader = VerifiedClient(factory, "recovery-reader", "reader@example.test");
        await owner.GetAsync("/api/auth/session");
        await reader.GetAsync("/api/auth/session");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(reader);
        var household = await CreateHouseholdAsync(owner);
        var id = Guid.NewGuid();
        var request = new { id, email = "reader@example.test", role = "Reader" };
        var first = await owner.PostAsJsonAsync($"/api/workspaces/{household.Id}/invitations", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var issued = (await first.Content.ReadFromJsonAsync<InvitationDto>())!;
        Assert.NotNull(issued.ShareUrl);
        var replay = await owner.PostAsJsonAsync($"/api/workspaces/{household.Id}/invitations", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Null((await replay.Content.ReadFromJsonAsync<InvitationDto>())!.ShareUrl);
        var token = TokenFrom(issued.ShareUrl!);
        await using (var db = Db(factory))
        {
            var stored = await db.WorkspaceInvitations.SingleAsync(x => x.Id == id);
            var raw = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token);
            Assert.Equal(32, raw.Length);
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(raw), stored.TokenHash);
            var audit = string.Join(" ", await db.WorkspaceAuditEvents.Where(x => x.WorkspaceId == household.Id).Select(x => x.ChangesJson).ToArrayAsync());
            Assert.DoesNotContain(token, audit, StringComparison.Ordinal);
            var remaining = stored.ExpiresAt - DateTimeOffset.UtcNow;
            Assert.True(remaining <= TimeSpan.FromDays(7) && remaining >= TimeSpan.FromDays(6.9));
        }

        using (var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{household.Id}/invitations/{id}") { Content = JsonContent.Create(new { version = 1 }) })
        {
            revoke.Headers.Add("X-CSRF-TOKEN", owner.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(revoke)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PostAsJsonAsync("/api/invitations/preview", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostTokenAsync(reader, token)).StatusCode);

        var expired = await CreateInvitationAsync(owner, household.Id, "expired@example.test");
        var expiredToken = TokenFrom(expired.ShareUrl);
        await using (var db = Db(factory))
        {
            var stored = await db.WorkspaceInvitations.SingleAsync(x => x.Id == expired.Id);
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        using var expiredClient = VerifiedClient(factory, "expired-reader", "expired@example.test");
        await expiredClient.GetAsync("/api/auth/session");
        await AddCsrfAsync(expiredClient);
        Assert.Equal(HttpStatusCode.NotFound, (await expiredClient.PostAsJsonAsync("/api/invitations/preview", new { token = expiredToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostTokenAsync(expiredClient, expiredToken)).StatusCode);
        var listed = await owner.GetFromJsonAsync<JsonElement[]>($"/api/workspaces/{household.Id}/invitations");
        Assert.Equal("Expired", Assert.Single(listed!, x => x.GetProperty("id").GetGuid() == expired.Id).GetProperty("status").GetString());
    }

    private static HttpClient VerifiedClient(ApiFactory factory, string subject, string email)
    {
        var client = factory.IdentityClient(subject);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        client.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");
        return client;
    }

    private static async Task AddCsrfAsync(HttpClient client)
    {
        if (client.DefaultRequestHeaders.Contains("X-CSRF-TOKEN")) return;
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
    }

    private static async Task<HttpResponseMessage> PostTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/accept") { Content = JsonContent.Create(new { token }) };
        request.Headers.Add("X-CSRF-TOKEN", client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
        return await client.SendAsync(request);
    }

    private static async Task<WorkspaceDto> CreateHouseholdAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar de prueba" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private static async Task<InvitationDto> CreateInvitationAsync(HttpClient owner, Guid householdId, string email, string role = "Reader")
    {
        using var response = await owner.PostAsJsonAsync($"/api/workspaces/{householdId}/invitations", new { id = Guid.NewGuid(), email, role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationDto>())!;
    }

    private static string TokenFrom(string shareUrl)
    {
        var fragment = new Uri(new Uri("https://localhost"), shareUrl).Fragment;
        return Uri.UnescapeDataString(fragment.TrimStart('#').Split('=', 2).Last());
    }

    private static async Task<Guid> UserIdAsync(ApiFactory factory, string subject)
    {
        await using var db = Db(factory);
        return await db.ApplicationUsers.Where(x => x.Subject == subject).Select(x => x.Id).SingleAsync();
    }

    private static AppDbContext Db(ApiFactory factory) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.TestConnection).Options);

    private sealed record WorkspaceDto(Guid Id, string Name, string Kind, string Role, int Version);
    private sealed record InvitationDto(Guid Id, string Email, string Role, string Status, DateTimeOffset ExpiresAt, int Version, string ShareUrl);
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

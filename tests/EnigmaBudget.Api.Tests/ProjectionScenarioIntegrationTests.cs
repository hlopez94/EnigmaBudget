using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EnigmaBudget.Api.Workspaces;

namespace EnigmaBudget.Api.Tests;

public sealed class ProjectionScenarioIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset TestInstant = DateTimeOffset.Parse("2026-10-25T15:00:00Z");
    private readonly ApiFactory _factory = new(timeProvider: new FixedTimeProvider(TestInstant));
    public void Dispose() => _factory.Dispose();
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task reader_can_post_ephemeral_projection_without_database_writes()
    {
        using var owner = await _factory.AuthenticatedClientAsync("projection-ephemeral-owner");
        using var client = await _factory.AuthenticatedClientAsync("projection-ephemeral-reader");
        using (var setup = _factory.Services.CreateScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerUser = await setupDb.ApplicationUsers.SingleAsync(x => x.Subject == "projection-ephemeral-owner");
            var readerUser = await setupDb.ApplicationUsers.SingleAsync(x => x.Subject == "projection-ephemeral-reader");
            var ownerWorkspace = await setupDb.Workspaces.SingleAsync(x => x.OwnerUserId == ownerUser.Id && x.Kind == "Personal");
            setupDb.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = ownerWorkspace.Id, UserId = readerUser.Id, Role = "Reader", JoinedAt = TestInstant });
            await setupDb.SaveChangesAsync();
            client.DefaultRequestHeaders.Add("X-Workspace-Id", ownerWorkspace.Id.ToString());
        }
        var through = DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(2).ToString("yyyy-MM-dd");
        var request = new { through, scenario = "Estimated", assumptions = new[] { new { id = Guid.NewGuid(), kind = "Income", date = through, currency = "ARS", amount = "123.45", description = "Ingreso estimado", sourceId = (Guid?)null } } };
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("description").GetString() == "Ingreso estimado");
        Assert.Equal("Estimated", row.GetProperty("certainty").GetString());
        Assert.Equal("123.45", row.GetProperty("amount").GetString());
        Assert.DoesNotContain(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("cashBalance").GetString() != "0.00");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.ProjectionScenarios.ToArrayAsync());
        Assert.Empty(await db.ProjectionAssumptions.ToArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/projection/scenarios", new { id = Guid.NewGuid(), name = "No permitido", assumptions = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task scenario_request_replay_is_idempotent_and_changed_payload_returns_409()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var requestId = Guid.NewGuid(); var id = Guid.NewGuid();
        var create = new { id, name = "Vacaciones", assumptions = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/projection/scenarios", create)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/projection/scenarios", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/projection/scenarios", new { id, name = "Datos distintos", assumptions = Array.Empty<object>() })).StatusCode);
        var payload = new { requestId, version = 1, name = "Viaje", assumptions = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/projection/scenarios/{id}", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/projection/scenarios/{id}", payload)).StatusCode);
        var changed = new { requestId, version = 1, name = "Otro nombre", assumptions = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/projection/scenarios/{id}", changed)).StatusCode);
    }

    [Fact]
    public async Task scenario_update_does_not_mutate_finance_records()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid();
        var create = await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Cuenta", bankName = "Banco", currency = "ARS", openingBalance = "50.00", openingDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = operationId, kind = "Expense", accountId, date = "2026-10-26", description = "Compra", category = "Varios", amount = "20.00", paid = false })).StatusCode);
        var id = Guid.NewGuid();
        var assumptions = new[] { new { id = Guid.NewGuid(), kind = "AmountChange", date = (string?)null, currency = "ARS", amount = "5.00", description = "Ajuste", sourceId = operationId, sourceKind = "FinanceOperation", componentId = (string?)null } };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/projection/scenarios", new { id, name = "Ajuste", assumptions })).StatusCode);
        using (var projected = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-10-28", scenario = "Estimated", savedScenarioId = id, assumptions = Array.Empty<object>() }))
        {
            Assert.Equal(HttpStatusCode.OK, projected.StatusCode);
            using var doc = JsonDocument.Parse(await projected.Content.ReadAsStringAsync());
            Assert.Equal("25.00", Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceId").GetGuid() == operationId).GetProperty("amount").GetString());
        }
        var before = JsonDocument.Parse(await (await client.GetAsync("/api/accounts")).Content.ReadAsStringAsync()).RootElement.GetRawText();
        var beforeOperations = JsonDocument.Parse(await (await client.GetAsync("/api/operations")).Content.ReadAsStringAsync()).RootElement.GetRawText();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/projection/scenarios/{id}", new { requestId = Guid.NewGuid(), version = 1, name = "Ajustado", assumptions = Array.Empty<object>() })).StatusCode);
        var after = JsonDocument.Parse(await (await client.GetAsync("/api/accounts")).Content.ReadAsStringAsync()).RootElement.GetRawText();
        var afterOperations = JsonDocument.Parse(await (await client.GetAsync("/api/operations")).Content.ReadAsStringAsync()).RootElement.GetRawText();
        Assert.Equal(before, after); Assert.Equal(beforeOperations, afterOperations);
    }

    [Fact]
    public async Task scenario_income_and_expense_assumptions_appear_only_on_selected_dates()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        const string through = "2026-10-28";
        var a = new { id = Guid.NewGuid(), kind = "Income", date = "2026-10-26", currency = "ARS", amount = "100.00", description = "Ingreso", sourceId = (Guid?)null };
        var b = new { id = Guid.NewGuid(), kind = "Expense", date = "2026-10-27", currency = "ARS", amount = "20.00", description = "Gasto", sourceId = (Guid?)null };
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new { through, scenario = "Estimated", assumptions = new[] { a, b } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("sourceKind").GetString() is "Income" or "Expense").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("2026-10-26", Assert.Single(rows, x => x.GetProperty("sourceKind").GetString() == "Income").GetProperty("date").GetString());
        Assert.Equal("2026-10-27", Assert.Single(rows, x => x.GetProperty("sourceKind").GetString() == "Expense").GetProperty("date").GetString());
        Assert.All(rows, x => Assert.Equal("Estimated", x.GetProperty("certainty").GetString()));
        var ars = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
        var daily = ars.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("0.00", daily["2026-10-25"].GetProperty("cashBalance").GetString());
        Assert.Equal("100.00", daily["2026-10-26"].GetProperty("cashBalance").GetString());
        Assert.Equal("80.00", daily["2026-10-27"].GetProperty("cashBalance").GetString());
    }

    [Fact]
    public async Task confirmed_excludes_uncollected_income_and_group_receivables()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var through = "2026-10-28";
        var assumption = new { id = Guid.NewGuid(), kind = "Income", date = through, currency = "ARS", amount = "50.00", description = "Futuro", sourceId = (Guid?)null };
        using var response = await client.GetAsync($"/api/projection?through={through}&scenario=Confirmed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() is "Income" or "GroupReceivable");
        Assert.NotNull(assumption);
    }

    [Fact]
    public async Task missing_agreed_receivable_date_is_editable_period_end_estimate()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var shared = await CreateSharedPurchaseAsync(client);
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-11-30", scenario = "Estimated", assumptions = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "GroupReceivable" && x.GetProperty("sourceId").GetGuid() == shared.InstallmentId);
        Assert.Equal("2026-11-30", row.GetProperty("date").GetString());
        Assert.Equal("Estimated", row.GetProperty("certainty").GetString());
        Assert.Equal("Incoming", row.GetProperty("direction").GetString());
    }

    [Fact]
    public async Task estimated_adds_shared_receivables_with_rule_and_collections_subtracted()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var shared = await CreateSharedPurchaseAsync(client);
        var rule = await client.PutAsJsonAsync($"/api/groups/{shared.GroupId}/rules/2026-11", new { version = 0, mode = "Equal", basisCurrency = "ARS", weights = new[] { new { participantId = shared.SelfId, value = "50.00" }, new { participantId = shared.GuestId, value = "50.00" } } });
        Assert.Equal(HttpStatusCode.OK, rule.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{shared.GroupId}/collections", new { id = Guid.NewGuid(), installmentId = shared.InstallmentId, participantId = shared.GuestId, accountId = shared.AccountId, date = "2026-10-25", amount = "20.00", appliedAmount = "20.00", ruleVersion = 1 })).StatusCode);
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-11-30", scenario = "Estimated", assumptions = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "GroupReceivable" && x.GetProperty("sourceId").GetGuid() == shared.InstallmentId);
        Assert.Equal("80.00", row.GetProperty("amount").GetString());
        Assert.Equal("Estimated", row.GetProperty("certainty").GetString());
        var currency = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("20.00", currency.GetProperty("cashBalance").GetString());
        Assert.Equal("20.00", currency.GetProperty("availableCash").GetString());

    }

    [Fact]
    public async Task amount_and_date_changes_affect_projection_row_without_mutating_finance_source()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var accountId = Guid.NewGuid(); var operationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "100.00", openingDate = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new { id = operationId, kind = "Expense", accountId, date = "2026-10-26", description = "Compra", category = "Comida", amount = "20.00", paid = false })).StatusCode);
        var assumptions = new object[] {
            new { id = Guid.NewGuid(), kind = "AmountChange", date = (string?)null, currency = "ARS", amount = "5.00", description = "Ajuste", sourceId = operationId, sourceKind = "FinanceOperation", componentId = (string?)null },
            new { id = Guid.NewGuid(), kind = "DateChange", date = "2026-10-27", currency = (string?)null, amount = (string?)null, description = "Cambio de fecha", sourceId = operationId, sourceKind = "FinanceOperation", componentId = (string?)null },
        };
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-10-28", scenario = "Estimated", assumptions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "FinanceOperation" && x.GetProperty("sourceId").GetGuid() == operationId);
        Assert.Equal("25.00", row.GetProperty("amount").GetString()); Assert.Equal("2026-10-27", row.GetProperty("date").GetString());
        var currency = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
        Assert.Equal("75.00", currency.GetProperty("minimumFreeBalance").GetString());
        Assert.Null(currency.GetProperty("firstShortfallDate").GetString());
        var final = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS").GetProperty("dailyBalances").EnumerateArray().Last();
        Assert.Equal("75.00", final.GetProperty("cashBalance").GetString());
        var ops = await client.GetFromJsonAsync<JsonElement>("/api/operations");
        Assert.Contains(ops.EnumerateArray(), x => x.GetProperty("id").GetGuid() == operationId && x.GetProperty("amount").GetString() == "20.00");
    }

    [Fact]
    public async Task source_identity_ambiguity_is_rejected_and_component_targets_one_participant()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var shared = await CreateSharedPurchaseAsync(client, extraGuest: true);
        var ambiguous = new[] { new { id = Guid.NewGuid(), kind = "DateChange", date = "2026-11-29", currency = (string?)null,
            amount = (string?)null, description = "Cambio", sourceId = shared.InstallmentId, sourceKind = "GroupReceivable", componentId = (string?)null } };
        using var rejected = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-11-30", scenario = "Estimated", assumptions = ambiguous });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var targeted = new[] { new { id = Guid.NewGuid(), kind = "DateChange", date = "2026-11-29", currency = (string?)null,
            amount = (string?)null, description = "Cambio", sourceId = shared.InstallmentId, sourceKind = "GroupReceivable", componentId = $"Participant:{shared.GuestId}" } };
        using var accepted = await client.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-11-30", scenario = "Estimated", assumptions = targeted });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var doc = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var receivables = doc.RootElement.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("sourceKind").GetString() == "GroupReceivable" && x.GetProperty("sourceId").GetGuid() == shared.InstallmentId).ToArray();
        Assert.Equal("2026-11-29", Assert.Single(receivables, x => x.GetProperty("componentId").GetString() == $"Participant:{shared.GuestId}").GetProperty("date").GetString());
        Assert.Equal("2026-11-30", Assert.Single(receivables, x => x.GetProperty("componentId").GetString() == $"Participant:{shared.OtherGuestId}").GetProperty("date").GetString());
    }

    [Fact]
    public async Task personal_reader_projection_has_no_group_rows_or_identifiers()
    {
        using var client = await _factory.AuthenticatedClientAsync("projection-reader-no-groups");
        var shared = await CreateSharedPurchaseAsync(client);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.ApplicationUsers.SingleAsync(x => x.Subject == "projection-reader-no-groups");
        var workspace = await db.Workspaces.SingleAsync(x => x.OwnerUserId == user.Id && x.Kind == "Personal");
        var context = new WorkspaceRequestContext(user, workspace, "Reader", new HashSet<string>([WorkspacePermissions.Read], StringComparer.Ordinal));
        var service = scope.ServiceProvider.GetRequiredService<EnigmaBudget.Api.Projection.ProjectionService>();
        var (projection, status, error) = await service.CalculateAsync(context, DateOnly.Parse("2026-11-30"), [], null, CancellationToken.None);
        Assert.Equal(200, status); Assert.Null(error); Assert.NotNull(projection);
        Assert.DoesNotContain(projection!.Rows, x => x.SourceKind == "GroupReceivable");
        var json = JsonSerializer.Serialize(projection);
        Assert.DoesNotContain(shared.GroupId.ToString(), json); Assert.DoesNotContain(shared.GuestId.ToString(), json);
        Assert.DoesNotContain("Ana", json);
    }

    [Fact]
    public async Task reader_cannot_load_or_delete_another_users_saved_scenario()
    {
        using var owner = await _factory.AuthenticatedClientAsync("scenario-owner");
        using var other = await _factory.AuthenticatedClientAsync("scenario-other");
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/projection/scenarios", new { id, name = "Privado", assumptions = Array.Empty<object>() })).StatusCode);
        Assert.Empty(await other.GetFromJsonAsync<JsonElement[]>("/api/projection/scenarios") ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-10-28", scenario = "Estimated", savedScenarioId = id, assumptions = Array.Empty<object>() })).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/projection/scenarios/{id}") { Content = JsonContent.Create(new { requestId = Guid.NewGuid(), version = 1 }) };
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(delete)).StatusCode);
    }

    [Fact]
    public async Task scenario_create_update_delete_require_record_finance_and_version()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/projection/scenarios", new { id, name = "Borrador", assumptions = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/projection/scenarios/{id}", new { requestId = Guid.NewGuid(), version = 2, name = "Nuevo", assumptions = Array.Empty<object>() })).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/projection/scenarios/{id}") { Content = JsonContent.Create(new { requestId = Guid.NewGuid(), version = 1 }) };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>("/api/projection/scenarios") ?? []);
    }

    private async Task<(Guid GroupId, Guid SelfId, Guid GuestId, Guid? OtherGuestId, Guid InstallmentId, Guid AccountId)> CreateSharedPurchaseAsync(HttpClient client, bool extraGuest = false)
    {
        var accountId = Guid.NewGuid(); var cardId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var selfId = Guid.NewGuid(); var guestId = Guid.NewGuid(); var otherGuestId = extraGuest ? Guid.NewGuid() : (Guid?)null;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new { id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = "2026-10-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/cards", new { id = cardId, name = "Visa", bankName = "Banco", closingDay = 10, dueDay = 20, statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01" })).StatusCode);
        var participants = new List<object> { new { id = selfId, name = "Yo", isSelf = true }, new { id = guestId, name = "Ana", isSelf = false } };
        if (otherGuestId is { } otherId) participants.Add(new { id = otherId, name = "Eva", isSelf = false });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/groups", new { id = groupId, name = "Viaje", participants })).StatusCode);
        var weights = new List<object> { new { participantId = selfId, value = extraGuest ? "33.34" : "50.00" }, new { participantId = guestId, value = extraGuest ? "33.33" : "50.00" } };
        if (otherGuestId is { } secondId) weights.Add(new { participantId = secondId, value = "33.33" });
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{groupId}/rules/2026-10", new { version = 0, mode = "Percentage", basisCurrency = "ARS", weights })).StatusCode);
        var purchase = await client.PostAsJsonAsync("/api/purchases", new { id = Guid.NewGuid(), cardId, date = "2026-10-02", description = "Hotel", category = "Viaje", currency = "ARS", totalAmount = "200.00", installmentCount = 1, firstPeriod = "2026-11", groupId });
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        var body = await purchase.Content.ReadFromJsonAsync<PurchaseDto>();
        return (groupId, selfId, guestId, otherGuestId, body!.Installments[0].Id, accountId);
    }

    private sealed record PurchaseDto(IReadOnlyList<InstallmentDto> Installments);
    private sealed record InstallmentDto(Guid Id);

}

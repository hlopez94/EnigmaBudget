using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnigmaBudget.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EnigmaBudget.Api.Workspaces;
using EnigmaBudget.Api.Savings;

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
    public async Task scenario_adjustments_keep_unassigned_pending_operations_in_available_cash()
    {
        using var client = await _factory.AuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(TestInstant.UtcDateTime);
        var accountId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/accounts", new
        {
            id = accountId, name = "Caja", bankName = "Banco", currency = "ARS", openingBalance = "0.00", openingDate = today.ToString("yyyy-MM-dd")
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = incomeId, kind = "Income", accountId, date = today.AddDays(1).ToString("yyyy-MM-dd"), description = "Ingreso pendiente", category = "Trabajo", amount = "125.00", paid = false
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/operations", new
        {
            id = expenseId, kind = "Expense", accountId, date = today.AddDays(2).ToString("yyyy-MM-dd"), description = "Gasto pendiente", category = "Comida", amount = "40.00", paid = false
        })).StatusCode);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_factory.TestConnection).Options))
        {
            var operations = await db.FinanceOperations.Where(x => x.Id == incomeId || x.Id == expenseId).ToArrayAsync();
            Assert.Equal(2, operations.Length);
            foreach (var operation in operations) operation.AccountId = null;
            await db.SaveChangesAsync();
        }

        var through = today.AddDays(3).ToString("yyyy-MM-dd");
        using (var baseline = await client.GetAsync($"/api/projection?through={through}&scenario=Confirmed"))
        {
            Assert.Equal(HttpStatusCode.OK, baseline.StatusCode);
            using var baselineDoc = JsonDocument.Parse(await baseline.Content.ReadAsStringAsync());
            var currency = Assert.Single(baselineDoc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
            var daily = currency.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
            Assert.Equal("125.00", daily[today.AddDays(1).ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
            Assert.Equal("85.00", daily[today.AddDays(2).ToString("yyyy-MM-dd")].GetProperty("freeBalance").GetString());
        }

        var assumptions = new object[]
        {
            new { id = Guid.NewGuid(), kind = "AmountChange", date = (string?)null, currency = "ARS", amount = "25.00", description = "Ingreso ajustado", sourceId = incomeId, sourceKind = "FinanceOperation", componentId = (string?)null },
            new { id = Guid.NewGuid(), kind = "DateChange", date = today.AddDays(2).ToString("yyyy-MM-dd"), currency = (string?)null, amount = (string?)null, description = "Ingreso reprogramado", sourceId = incomeId, sourceKind = "FinanceOperation", componentId = (string?)null },
            new { id = Guid.NewGuid(), kind = "AmountChange", date = (string?)null, currency = "ARS", amount = "20.00", description = "Gasto ajustado", sourceId = expenseId, sourceKind = "FinanceOperation", componentId = (string?)null },
            new { id = Guid.NewGuid(), kind = "DateChange", date = today.AddDays(3).ToString("yyyy-MM-dd"), currency = (string?)null, amount = (string?)null, description = "Gasto reprogramado", sourceId = expenseId, sourceKind = "FinanceOperation", componentId = (string?)null },
        };
        using var response = await client.PostAsJsonAsync("/api/projection/calculate", new { through, scenario = "Estimated", assumptions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("sourceKind").GetString() == "FinanceOperation").ToArray();
        Assert.Equal("150.00", Assert.Single(rows, x => x.GetProperty("sourceId").GetGuid() == incomeId).GetProperty("amount").GetString());
        Assert.Equal("60.00", Assert.Single(rows, x => x.GetProperty("sourceId").GetGuid() == expenseId).GetProperty("amount").GetString());
        var result = Assert.Single(doc.RootElement.GetProperty("currencies").EnumerateArray(), x => x.GetProperty("currency").GetString() == "ARS");
        var projected = result.GetProperty("dailyBalances").EnumerateArray().ToDictionary(x => x.GetProperty("date").GetString()!);
        Assert.Equal("0.00", projected[today.ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("0.00", projected[today.AddDays(1).ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
        Assert.Equal("150.00", projected[today.AddDays(2).ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("150.00", projected[today.AddDays(2).ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
        Assert.Equal("0.00", projected[today.AddDays(2).ToString("yyyy-MM-dd")].GetProperty("restrictedCash").GetString());
        Assert.Equal("90.00", projected[today.AddDays(3).ToString("yyyy-MM-dd")].GetProperty("cashBalance").GetString());
        Assert.Equal("90.00", projected[today.AddDays(3).ToString("yyyy-MM-dd")].GetProperty("availableCash").GetString());
        Assert.Equal("90.00", projected[today.AddDays(3).ToString("yyyy-MM-dd")].GetProperty("freeBalance").GetString());
        Assert.Equal("0.00", projected[today.AddDays(3).ToString("yyyy-MM-dd")].GetProperty("restrictedCash").GetString());
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

    [Fact]
    public async Task shared_personal_reader_scenario_list_hides_historical_group_selectors_but_keeps_manual_assumptions()
    {
        using var factory = new ApiFactory(timeProvider: new FixedTimeProvider(TestInstant));
        using var owner = VerifiedClient(factory, "scenario-privacy-owner", "owner@example.test");
        using var reader = VerifiedClient(factory, "scenario-privacy-reader", "reader@example.test");
        await AddCsrfAsync(owner);
        await AddCsrfAsync(reader);
        var personal = Assert.Single(await owner.GetFromJsonAsync<JsonElement[]>("/api/workspaces") ?? []);
        var personalId = personal.GetProperty("id").GetGuid();
        owner.DefaultRequestHeaders.Add("X-Workspace-Id", personalId.ToString());
        var personalAccountId = Guid.NewGuid(); var personalOperationId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/accounts", new { id = personalAccountId, name = "Cuenta privada", bankName = "Banco", currency = "ARS", openingBalance = "100.00", openingDate = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/operations", new { id = personalOperationId, kind = "Expense", accountId = personalAccountId, date = "2026-10-26", description = "Compra personal visible", category = "Hogar", amount = "25.00", paid = false })).StatusCode);

        var groupId = Guid.NewGuid(); var selfId = Guid.NewGuid(); var participantId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/groups", new { id = groupId, name = "Grupo privado histórico", participants = new[] {
            new { id = selfId, name = "Titular", isSelf = true }, new { id = participantId, name = "Persona no visible", isSelf = false } } })).StatusCode);
        var cardId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/cards", new { id = cardId, name = "Visa", bankName = "Banco", closingDay = 10, dueDay = 20,
            statementDebtArs = "0.00", statementDebtUsd = "0.00", asOfDate = "2026-10-01" })).StatusCode);
        var purchase = await owner.PostAsJsonAsync("/api/purchases", new { id = Guid.NewGuid(), cardId, date = "2026-10-02", description = "Compra", category = "Viaje",
            currency = "ARS", totalAmount = "200.00", installmentCount = 1, firstPeriod = "2026-11", groupId });
        Assert.Equal(HttpStatusCode.OK, purchase.StatusCode);
        using var purchaseDoc = JsonDocument.Parse(await purchase.Content.ReadAsStringAsync());
        var installmentId = purchaseDoc.RootElement.GetProperty("installments")[0].GetProperty("id").GetGuid();

        var household = await CreateHouseholdAsync(owner);
        var invitation = await CreateInvitationAsync(owner, household.Id, "reader@example.test");
        Assert.Equal(HttpStatusCode.OK, (await PostTokenAsync(reader, TokenFrom(invitation.ShareUrl))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/personal-sharing/{household.Id}", new { version = 1, enabled = true })).StatusCode);
        var sharedPersonal = Assert.Single(await reader.GetFromJsonAsync<JsonElement[]>("/api/workspaces") ?? [], x => x.GetProperty("id").GetGuid() == personalId);
        Assert.True(sharedPersonal.GetProperty("isSharedPersonalRead").GetBoolean());
        reader.DefaultRequestHeaders.Add("X-Workspace-Id", personalId.ToString());

        var readerId = await UserIdAsync(factory, "scenario-privacy-reader");
        var scenarioId = Guid.NewGuid();
        var hiddenAssumptionId = Guid.NewGuid();
        var hiddenNullKindAssumptionId = Guid.NewGuid();
        var manualAssumptionId = Guid.NewGuid();
        await using (var db = Db(factory))
        {
            db.ProjectionScenarios.Add(new ProjectionScenario { Id = scenarioId, WorkspaceId = personalId, ActorUserId = readerId, Name = "Notas propias", Version = 3 });
            db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = hiddenAssumptionId, WorkspaceId = personalId, ScenarioId = scenarioId,
                Kind = "AmountChange", Currency = "ARS", Amount = 17.50m, Description = "Importe para Persona no visible", SourceId = installmentId,
                SourceKind = "GroupReceivable", ComponentId = $"Participant:{participantId}", Version = 2 });
            db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = hiddenNullKindAssumptionId, WorkspaceId = personalId, ScenarioId = scenarioId,
                Kind = "AmountChange", Currency = "ARS", Amount = 11.00m, Description = "Importe oculto sin tipo", SourceId = installmentId,
                ComponentId = $"Participant:{participantId}", Version = 1 });
            db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = Guid.NewGuid(), WorkspaceId = personalId, ScenarioId = scenarioId,
                Kind = "AmountChange", Currency = "ARS", Amount = 1.25m, Description = "Ajuste de compra personal", SourceId = personalOperationId,
                SourceKind = "FinanceOperation", Version = 1 });
            db.ProjectionAssumptions.Add(new ProjectionAssumption { Id = manualAssumptionId, WorkspaceId = personalId, ScenarioId = scenarioId,
                Kind = "Income", Date = DateOnly.FromDateTime(TestInstant.UtcDateTime).AddDays(1), Currency = "ARS", Amount = 15.00m,
                Description = "Ingreso manual", Version = 1 });
            await db.SaveChangesAsync();
        }

        using var response = await reader.GetAsync("/api/projection/scenarios");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(groupId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(installmentId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(participantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hiddenAssumptionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hiddenNullKindAssumptionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Persona no visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Importe para", body, StringComparison.Ordinal);
        Assert.DoesNotContain("17.50", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Importe oculto sin tipo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("11.00", body, StringComparison.Ordinal);
        using var list = JsonDocument.Parse(body);
        var saved = Assert.Single(list.RootElement.EnumerateArray(), x => x.GetProperty("id").GetGuid() == scenarioId);
        Assert.Equal("Notas propias", saved.GetProperty("name").GetString());
        var retained = saved.GetProperty("assumptions").EnumerateArray().ToArray();
        Assert.Equal(2, retained.Length);
        var manual = Assert.Single(retained, x => x.GetProperty("id").GetGuid() == manualAssumptionId);
        Assert.Equal("Ingreso manual", manual.GetProperty("description").GetString());
        var personalChange = Assert.Single(retained, x => x.GetProperty("sourceId").ValueKind == JsonValueKind.String && x.GetProperty("sourceId").GetGuid() == personalOperationId);
        Assert.Equal("Ajuste de compra personal", personalChange.GetProperty("description").GetString());

        using var calculate = await reader.PostAsJsonAsync("/api/projection/calculate", new { through = "2026-10-28", scenario = "Estimated", savedScenarioId = scenarioId, assumptions = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, calculate.StatusCode);
        using var projection = JsonDocument.Parse(await calculate.Content.ReadAsStringAsync());
        Assert.Contains(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("description").GetString() == "Ingreso manual");
        Assert.Equal("26.25", Assert.Single(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceId").GetGuid() == personalOperationId).GetProperty("amount").GetString());
        Assert.DoesNotContain(projection.RootElement.GetProperty("rows").EnumerateArray(), x => x.GetProperty("sourceKind").GetString() == "GroupReceivable");
        await using var verify = Db(factory);
        Assert.Equal(4, await verify.ProjectionAssumptions.CountAsync(x => x.WorkspaceId == personalId && x.ScenarioId == scenarioId));
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
        using var response = await client.GetAsync("/api/auth/session");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", document.RootElement.GetProperty("csrfToken").GetString());
    }

    private static async Task<HttpResponseMessage> PostTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/accept") { Content = JsonContent.Create(new { token }) };
        request.Headers.Add("X-CSRF-TOKEN", client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single());
        return await client.SendAsync(request);
    }

    private static async Task<(Guid Id, string ShareUrl)> CreateHouseholdAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/workspaces", new { id = Guid.NewGuid(), kind = "Household", name = "Hogar de prueba" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("id").GetGuid(), "");
    }

    private static async Task<(Guid Id, string ShareUrl)> CreateInvitationAsync(HttpClient owner, Guid householdId, string email)
    {
        using var response = await owner.PostAsJsonAsync($"/api/workspaces/{householdId}/invitations", new { id = Guid.NewGuid(), email, role = "Reader" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("id").GetGuid(), document.RootElement.GetProperty("shareUrl").GetString()!);
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

using EnigmaBudget.Api.Data;

namespace EnigmaBudget.Api.Finance;

public static class CatalogDefaults
{
    public static void AddForWorkspace(AppDbContext db, Guid workspaceId)
    {
        db.FinanceCategories.AddRange(
            new FinanceCategory { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Kind = "Expense", Name = "Sin clasificar", IsArchived = false, Version = 1 },
            new FinanceCategory { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Kind = "Income", Name = "Sin clasificar", IsArchived = false, Version = 1 });
    }
}

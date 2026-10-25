using EnigmaBudget.Api.Workspaces;

namespace EnigmaBudget.Api.Tests;

public sealed class WorkspaceAuthorizationTests
{
    [Theory]
    [InlineData("GET", "/api/savings/goals", WorkspacePermissions.Read)]
    [InlineData("POST", "/api/projection/calculate", WorkspacePermissions.Read)]
    [InlineData("GET", "/api/projection/scenarios", WorkspacePermissions.Read)]
    [InlineData("POST", "/api/projection/scenarios", WorkspacePermissions.RecordFinance)]
    [InlineData("PUT", "/api/projection/scenarios/123", WorkspacePermissions.RecordFinance)]
    [InlineData("DELETE", "/api/projection/scenarios/123", WorkspacePermissions.RecordFinance)]
    [InlineData("POST", "/api/savings/goals", WorkspacePermissions.ManageResources)]
    [InlineData("PUT", "/api/savings/goals/123", WorkspacePermissions.ManageResources)]
    [InlineData("POST", "/api/savings/goals/123/close", WorkspacePermissions.ManageResources)]
    [InlineData("POST", "/api/savings/allocations", WorkspacePermissions.RecordFinance)]
    [InlineData("POST", "/api/savings/allocations/123/release", WorkspacePermissions.RecordFinance)]
    [InlineData("POST", "/api/savings/applications", WorkspacePermissions.RecordFinance)]
    [InlineData("POST", "/api/savings/applications/123/reverse", WorkspacePermissions.CorrectHistory)]
    [InlineData("PUT", "/api/accounts/123/liquidity", WorkspacePermissions.ManageResources)]
    [InlineData("PUT", "/api/savings/accounts/123/liquidity", WorkspacePermissions.ManageResources)]
    public void Savings_route_permissions_are_explicit(string method, string path, string expected)
    {
        var routeClassifier = typeof(FinanceWorkspaceFilter).GetMethod("RequiredPermission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, routeClassifier.Invoke(null, [method, new Microsoft.AspNetCore.Http.PathString(path)]));
    }

    [Theory]
    [InlineData("Owner", true, true, true, true, true, true)]
    [InlineData("Admin", true, true, true, true, true, false)]
    [InlineData("Member", true, true, true, false, false, false)]
    [InlineData("Reader", true, true, false, false, false, false)]
    public void Workspace_roles_only_receive_their_server_side_financial_permissions(
        string role, bool read, bool readGroups, bool recordFinance, bool manageResources, bool correctHistory, bool managePrivacy)
    {
        var permissions = WorkspacePermissions.For(role, personalOwner: role == "Owner");

        Assert.Equal(read, permissions.Contains(WorkspacePermissions.Read));
        Assert.Equal(readGroups, permissions.Contains(WorkspacePermissions.ReadGroups));
        Assert.Equal(recordFinance, permissions.Contains(WorkspacePermissions.RecordFinance));
        Assert.Equal(manageResources, permissions.Contains(WorkspacePermissions.ManageResources));
        Assert.Equal(correctHistory, permissions.Contains(WorkspacePermissions.CorrectHistory));
        Assert.Equal(role is "Owner" or "Admin", permissions.Contains(WorkspacePermissions.ManageMembers));
        Assert.Equal(managePrivacy, permissions.Contains(WorkspacePermissions.ManagePrivacy));
    }

    [Fact]
    public void Household_ownership_and_administration_do_not_grant_personal_privacy_controls()
    {
        Assert.False(WorkspacePermissions.For("Owner").Contains(WorkspacePermissions.ManagePrivacy));
        Assert.False(WorkspacePermissions.For("Admin", personalOwner: true).Contains(WorkspacePermissions.ManagePrivacy));
        Assert.True(WorkspacePermissions.For("Owner", personalOwner: true).Contains(WorkspacePermissions.ManagePrivacy));
    }
}

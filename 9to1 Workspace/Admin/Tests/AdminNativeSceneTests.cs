using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
namespace NineToOne.Admin.Tests;
[CollectionDefinition("AdminNative",DisableParallelization=true)]public sealed class AdminNativeCollection{}
[Collection("AdminNative")]
public sealed class AdminNativeSceneTests
{
    [Fact]
    public async Task Real_authored_cui_mounts_on_retained_native_backend()
    {
        await using var session=HeadlessUnitTestSession.StartNew(typeof(AdminTestApplication));
        await session.Dispatch(async()=>
        {
            var model=new CuiViewModel();model.Set("AccountName","Explicit fictional scene fixture");model.Set("Status","Ready fixture");
            model.Set("OrganisationName","Fixture organisation");model.Set("OrganisationID","");model.Set("BillingSummary","USD 5.00 per month");
            model.Set("Organisations",Array.Empty<AdminOrganisationRow>());model.Set("Members",Array.Empty<AdminMemberRow>());model.Set("Policy",Array.Empty<AdminPolicyRow>());model.Set("Audit",Array.Empty<object>());
            model.On("Refresh",_=>{});model.On("OpenOrganisation",_=>{});model.On("RefreshAudit",_=>{});model.On("RefreshBilling",_=>{});
            using var host=new CuiSceneHost();Assert.Equal(CuiSceneAvailabilityState.Ready,(await host.ShowAsync(AdminNativeScene.Create(model,model,new FixtureReady(true)))).State);
            Assert.DoesNotContain(host.Diagnostics,d=>d.Severity==CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            Assert.Contains(host.GetLogicalDescendants().OfType<TextBlock>(),t=>t.Text=="9to1 Admin");
            Assert.Contains(host.GetLogicalDescendants().OfType<Button>(),b=>Equals(b.Content,"Refresh organisations"));
            Assert.Contains(host.GetLogicalDescendants().OfType<Button>(),b=>Equals(b.Content,"Refresh billing and recovery status"));
            return true;
        },default);
    }
    [Fact]
    public async Task Missing_home_never_mounts_account_or_management_actions()
    {
        await using var session=HeadlessUnitTestSession.StartNew(typeof(AdminTestApplication));
        await session.Dispatch(async()=>
        {
            var model=new CuiViewModel();using var host=new CuiSceneHost();
            Assert.Equal(CuiSceneAvailabilityState.Unavailable,(await host.ShowAsync(AdminNativeScene.Create(model,model,new FixtureReady(false)))).State);
            Assert.Empty(host.GetLogicalDescendants().OfType<Button>());
            Assert.Contains(host.GetLogicalDescendants().OfType<TextBlock>(),t=>t.Text=="Actual Home bridge required");return true;
        },default);
    }
    private sealed class FixtureReady(bool ready):ICuiSceneReadiness
    {public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)=>ValueTask.FromResult(new CuiSceneAvailability(ready?CuiSceneAvailabilityState.Ready:CuiSceneAvailabilityState.Unavailable,"explicit-test-only-readiness","Actual Home bridge required"));}
}
public sealed class AdminTestApplication:Application{}

using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed partial class WorkspaceTaskRunToolOwnerTests
{
    [Fact]
    public async Task Workspace_prepare_denies_actual_model_EditFiles_without_original_route_restriction()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            rig.Models.Permissions.Policy = new([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, rig.Models.Local.Model.Name, ModelPermissionScope.ThisDevice, RestrictedModelCapability.EditFiles)]);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.PrepareAsync(Call("write_file", new { path = "denied.txt", content = "no effect" })));
            Assert.Equal(0, rig.Service.Moves); Assert.False(File.Exists(Path.Combine(rig.Root, "denied.txt")));
        });
    }
    [Fact]
    public async Task Model_permission_revoked_after_prepare_blocks_actual_original_native_move()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            rig.Policy.Grant("capability:write-file");
            var call = Call("write_file", new { path = "denied.txt", content = "no effect" }); var prep = await rig.PrepareAsync(call);
            rig.Models.Permissions.Policy = new([ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, rig.Models.Local.Model.Name, ModelPermissionScope.ThisDevice, RestrictedModelCapability.EditFiles)]);
            var run = rig.Track(rig.Owner.ExecuteOriginalAsync(prep, ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, prep, ct), default));
            var error = await Record.ExceptionAsync(() => run); Assert.NotNull(error); rig.Expect(error!);
            Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
            Assert.Equal(0, rig.Service.Moves); Assert.False(File.Exists(Path.Combine(rig.Root, "denied.txt")));
            Assert.Same(run, rig.Owner.ExecuteOriginalAsync(prep, _ => throw new InvalidOperationException("No replay"), default));
        });
    }
    [Fact]
    public async Task Workspace_original_body_refuses_a_private_preparation_without_actual_action_ACK()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            rig.Policy.Grant("capability:write-file");
            var current = (await rig.Coordinator.GetAsync(rig.Admission.Snapshot.TaskId, default))!;
            var call = Call("write_file", new { path = "unacknowledged.txt", content = "no effect" });
            var preparation = await rig.Owner.PrepareOriginalAsync(rig.Admission, current, Guid.NewGuid(), call,
                ToolRuntimeKind.Workspace, PermissionMode.Ask, rig.Root, default);
            var bodies = 0;
            var original = rig.Track(rig.Owner.ExecuteOriginalAsync(preparation, ct =>
            { bodies++; return rig.Runtime.ExecuteOriginalAsync(rig.Root, call, preparation, ct); }, default));
            var error = await Record.ExceptionAsync(() => original); Assert.NotNull(error); rig.Expect(error!);
            Assert.Equal(0, bodies); Assert.Equal(0, rig.Service.Moves);
            Assert.False(File.Exists(Path.Combine(rig.Root, "unacknowledged.txt")));
            Assert.Same(original, rig.Owner.ExecuteOriginalAsync(preparation, _ => throw new InvalidOperationException("No replay"), default));
        });
    }
    [Fact]
    public async Task Genuine_action_receipt_model_policy_and_final_gate_preserve_one_actual_write()
    {
        await RunOriginalOwnerControlAsync(async rig =>
        {
            rig.Policy.Grant("capability:write-file");
            var call = Call("write_file", new { path = "original.txt", content = "exact original" });
            var preparation = await rig.PrepareAsync(call);
            var source = (ITaskRunOriginalActionAdmissionSource)rig.Coordinator;
            var receipt = source.RequireOriginalActionAdmission(preparation, rig.Admission);
            Assert.Equal(preparation.ActionId, receipt.ActionId);
            var original = rig.Track(rig.Owner.ExecuteOriginalAsync(preparation,
                ct => rig.Runtime.ExecuteOriginalAsync(rig.Root, call, preparation, ct), default));
            var result = await original;
            Assert.Equal(1, rig.Service.Moves); Assert.Equal("exact original", File.ReadAllText(Path.Combine(rig.Root, "original.txt")));
            Assert.NotNull(result.OwnerReceiptReference);
            Assert.Same(original, rig.Owner.ExecuteOriginalAsync(preparation, _ => throw new InvalidOperationException("No replay"), default));
        });
    }
}

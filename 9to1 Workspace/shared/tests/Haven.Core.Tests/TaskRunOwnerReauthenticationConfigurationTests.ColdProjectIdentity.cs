using System.Reflection;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

// Policy-dispatch controls only. Detached material never enters Prepare/Activate and
// never supplies the mandatory private journal/Home Context validation or a permission.
public sealed partial class TaskRunOwnerReauthenticationConfigurationTests
{
    [Fact]
    public async Task Project_identity_policy_after_private_proof_has_no_unavailable_original_model_selection()
    {
        var reads = 0; var provider = new PolicyProvider("failed-original", () => { reads++; throw new IOException("No old model lease in identity activation"); });
        var policy = new PolicyStore(); var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        await RunAcceptedPolicyControl(authority, sources, [], async () =>
        {
            await ReadActualProjectIdentityPolicy(authority, sources, ProjectPolicyMaterial(provider.Model));
            Assert.Equal(0, reads); Assert.Equal(1, policy.Reads); Assert.Empty(sources.OriginalErrors);
            Assert.False(authority.HasOriginalColdRecoveryComposition(null!, null!));
        });
    }
    [Fact]
    public async Task Old_identity_dispatch_still_refuses_project_fields_without_private_resource_proof()
    {
        var provider = new PolicyProvider("unused", () => throw new IOException("No catalogue expected"));
        var policy = new PolicyStore(); var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority); var known = new List<Exception>();
        await RunAcceptedPolicyControl(authority, sources, known, async () =>
        {
            var read = ReadActualColdActivationPolicy(authority, sources, ProjectPolicyMaterial(provider.Model));
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read); known.Add(error);
            Assert.True(read.IsFaulted); Assert.Equal(0, policy.Reads);
            Assert.Contains(sources.OriginalErrors, value => ReferenceEquals(value, error));
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_policy_refuses_schema_one_or_mismatched_input_before_current_policy(bool schemaOne)
    {
        var provider = new PolicyProvider("unused", () => throw new IOException("No catalogue expected"));
        var policy = new PolicyStore(); var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority); var known = new List<Exception>();
        await RunAcceptedPolicyControl(authority, sources, known, async () =>
        {
            var capsule = ProjectPolicyMaterial(provider.Model);
            capsule = schemaOne ? capsule with { SchemaVersion = 1, Boundary = TaskRunColdBoundaryKind.NeverStartedAcceptedInput }
                : capsule with { OriginalInput = capsule.OriginalInput with { WorkspaceRoot = "/different-root" } };
            var read = ReadActualProjectIdentityPolicy(authority, sources, capsule);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read); known.Add(error);
            Assert.True(read.IsFaulted); Assert.Equal(0, policy.Reads);
            Assert.Contains(sources.OriginalErrors, value => ReferenceEquals(value, error));
        });
    }
    [Fact]
    public async Task Project_identity_policy_retains_actual_faulted_OCE_and_direct_sibling()
    {
        var cancellation = new OperationCanceledException("actual policy fault"); var sibling = new IOException("actual policy sibling");
        var raw = new TaskCompletionSource<ModelPermissionPolicy>(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([cancellation, sibling]);
        var policy = new PolicyStore { Read = () => raw.Task }; var provider = new PolicyProvider("unused", () => throw new IOException("No catalogue expected"));
        var authority = PolicyAuthority(policy, provider); var sources = PolicySources(authority);
        await RunAcceptedPolicyControl(authority, sources, [cancellation, sibling], async () =>
        {
            var read = ReadActualProjectIdentityPolicy(authority, sources, ProjectPolicyMaterial(provider.Model));
            var error = await Assert.ThrowsAsync<AggregateException>(() => read);
            Assert.True(read.IsFaulted); Assert.False(read.IsCanceled);
            Assert.Contains(sources.OriginalTasks, value => ReferenceEquals(value, raw.Task));
            Assert.Contains(AcceptedPolicyLeaves(error), value => ReferenceEquals(value, cancellation));
            Assert.Contains(AcceptedPolicyLeaves(error), value => ReferenceEquals(value, sibling));
        });
    }
    private static TaskRunColdCapsule ProjectPolicyMaterial(ProviderModelDescriptor model)
    {
        var container = Guid.NewGuid(); var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "project policy control", container, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var input = PolicyInput(model) with { Conversation = conversation, WorkspaceRoot = "/controlled-project", ProjectContext = "{\"controlled\":true}", ProjectInstructions = "exact instructions" };
        return AcceptedPolicyMaterial(model) with { AcceptedConversation = conversation, OriginalInput = input,
            OriginalProjectIdentity = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, null, input.WorkspaceRoot,
                input.ProjectContext, container, new('a', 64), input.ProjectInstructions, new('b', 64), "controlled-native-expectation",
                new("home-profile:control", "control-profile", null, null, "control-observation")) };
    }
    private static Task ReadActualProjectIdentityPolicy(TaskRunPermissionAuthority authority, CloudflareOriginalTaskLedger sources, TaskRunColdCapsule capsule) =>
        typeof(TaskRunPermissionAuthority).GetMethod("ReadColdProjectPolicyAfterProofAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<CloudflareOriginalTaskLedger, TaskRunColdCapsule, CancellationToken, Task>>(authority)(sources, capsule, CancellationToken.None);
}

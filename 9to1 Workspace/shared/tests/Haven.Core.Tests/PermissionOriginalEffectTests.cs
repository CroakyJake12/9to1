using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>Real central policy writer gate and native effect controls; no OS/Home/account grants.</summary>
public sealed class PermissionOriginalEffectTests
{
    [Fact]
    public void Revocation_before_actual_move_preserves_existing_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-original-effect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var old = Path.Combine(root, "document"); var staged = Path.Combine(root, "staged");
        try
        {
            File.WriteAllText(old, "before"); File.WriteAllText(staged, "after");
            var gate = new PermissionDecisionEngine(); gate.Grant("workspace.write");
            Assert.Equal(PermissionDecisionKind.Allowed, gate.Evaluate("workspace.write", CapabilityRiskClass.Consequential, true, "stage").Kind);
            gate.Revoke("workspace.write");
            Assert.Throws<UnauthorizedAccessException>(() => gate.RunOriginalEffect("workspace.write", CapabilityRiskClass.Consequential, true,
                "commit", () => { File.Move(staged, old, true); return 1; }));
            Assert.Equal("before", File.ReadAllText(old)); Assert.Equal("after", File.ReadAllText(staged));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Changed_policy_is_rechecked_before_low_risk_effect()
    {
        var gate = new PermissionDecisionEngine(); gate.SetPolicy(HavenPermissionPolicy.AskWithHighRisk);
        Assert.Equal(PermissionDecisionKind.Allowed, gate.Evaluate("local.create", CapabilityRiskClass.Low, true, "stage").Kind);
        gate.SetPolicy(HavenPermissionPolicy.AlwaysAsk);
        var effects = 0;
        Assert.Throws<UnauthorizedAccessException>(() => gate.RunOriginalEffect("local.create", CapabilityRiskClass.Low, true,
            "commit", () => ++effects));
        Assert.Equal(0, effects);
    }

    [Fact]
    public async Task Actual_effect_finishes_before_other_thread_revocation_then_next_effect_is_denied()
    {
        var gate = new PermissionDecisionEngine(); gate.Grant("native.start");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var effect = Task.Run(() => gate.RunOriginalEffect("native.start", CapabilityRiskClass.Restricted, true, "start", () =>
        { entered.TrySetResult(); release.Wait(); return 7; }));
        Task? revoke = null;
        try
        {
            await entered.Task;
            revoke = Task.Run(() => { revoking.TrySetResult(); gate.Revoke("native.start"); });
            await revoking.Task;
            Assert.False(effect.IsCompleted); Assert.False(revoke.IsCompleted);
        }
        finally { release.Set(); await effect; if (revoke is not null) await revoke; }
        Assert.Equal(7, await effect);
        Assert.Throws<UnauthorizedAccessException>(() => gate.RunOriginalEffect("native.start", CapabilityRiskClass.Restricted, true, "next", () => 8));
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("revoke")]
    [InlineData("policy")]
    public void Same_thread_policy_reentry_is_refused_and_original_native_error_remains_exact(string operation)
    {
        var gate = new PermissionDecisionEngine(); gate.Grant("native.write");
        var original = new IOException("Actual native failure");
        var thrown = Assert.Throws<IOException>(() => gate.RunOriginalEffect<int>("native.write", CapabilityRiskClass.Consequential, true, "write", () =>
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (operation == "grant") gate.Grant("other");
                else if (operation == "revoke") gate.Revoke("native.write");
                else gate.SetPolicy(HavenPermissionPolicy.AlwaysAllow);
            });
            throw original;
        }));
        Assert.Same(original, thrown);
        gate.Revoke("native.write"); // Genuine effect finally released the same writer gate/reentry state.
        Assert.Equal(PermissionDecisionKind.Ask, gate.Evaluate("native.write", CapabilityRiskClass.Consequential, true, "again").Kind);
    }
}

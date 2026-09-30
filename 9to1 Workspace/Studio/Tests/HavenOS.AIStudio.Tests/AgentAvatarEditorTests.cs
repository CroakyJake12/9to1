using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using NineToOne.Dulche.Den;
using Xunit;
namespace HavenOS.AIStudio.Tests;

[CollectionDefinition("StudioNative", DisableParallelization = true)] public sealed class StudioNativeCollection { }
[Collection("StudioNative")]
public sealed class AgentAvatarEditorTests
{
    [Fact]
    public async Task Visual_edits_preview_save_reopen_and_conflict_use_one_canonical_agent()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-studio-avatar-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            var agent = await den.SaveAsync(new AgentDefinitionRecord { Id = "coder", NamespaceId = "personal", DisplayName = "Coder", Version = "1" }, 0, "create");
            var service = new AgentPresentationService(den, new FixtureAssets());
            var editor = new AgentAvatarEditor(service);
            await editor.OpenAsync("personal", "coder");
            editor.Bindings.Set("StaticAsset", "asset:static"); editor.Bindings.Set("AccessibleName", "Coder avatar");
            editor.Bindings.Set("Animated", false);
            await editor.DispatchAsync("SetAvatarIdentity", null);
            foreach (var state in new[] { "idle", "coding", "laugh" })
            {
                editor.Bindings.Set("StateID", state); editor.Bindings.Set("StateLabel", state); editor.Bindings.Set("StateAsset", "asset:" + state);
                await editor.DispatchAsync("AddAvatarState", null);
            }
            editor.Bindings.Set("InitialState", "idle"); editor.Bindings.Set("Animated", true);
            await editor.DispatchAsync("SetAvatarIdentity", null);
            editor.Bindings.Set("FromState", "idle"); editor.Bindings.Set("ToState", "coding"); editor.Bindings.Set("TransitionEvent", "activity.coding");
            await editor.DispatchAsync("SetAvatarTransition", null);
            editor.Bindings.Set("ReactionEvent", "conversation.joke"); editor.Bindings.Set("ReactionState", "laugh");
            await editor.DispatchAsync("SetAvatarReaction", null);
            editor.Bindings.Set("StateID", "idle"); editor.Bindings.Set("PreviewEvent", "activity.coding"); editor.Bindings.Set("AvatarActivity", "Coding");
            await editor.DispatchAsync("PreviewAvatar", null);
            Assert.Equal("asset:coding", editor.Bindings.Get("PreviewAsset"));
            Assert.Null((await den.GetAsync<AgentDefinitionRecord>("personal", "coder"))!.Presentation);
            await editor.DispatchAsync("SaveAvatar", null);
            var reopened = new AgentAvatarEditor(service); await reopened.OpenAsync("personal", "coder");
            Assert.Equal(3, reopened.Draft!.States.Count);
            reopened.Bindings.Set("PreviewEvent", "conversation.joke"); await reopened.DispatchAsync("PreviewAvatar", null);
            Assert.Equal("asset:laugh", reopened.Bindings.Get("PreviewAsset"));
            reopened.Bindings.Set("ReducedMotion", true); await reopened.DispatchAsync("PreviewAvatar", null);
            Assert.Equal("asset:static", reopened.Bindings.Get("PreviewAsset"));
            await editor.DispatchAsync("SaveAvatar", null);
            var conflict = await Assert.ThrowsAsync<DenException>(() => reopened.DispatchAsync("SaveAvatar", null).AsTask());
            Assert.Equal(DenErrorCode.Conflict, conflict.Code);
            await reopened.OpenAsync("personal", "coder");
            reopened.Bindings.Set("StaticAsset", "asset:denied"); await reopened.DispatchAsync("SetAvatarIdentity", null);
            Assert.Equal(DenErrorCode.Forbidden, (await Assert.ThrowsAsync<DenException>(() => reopened.DispatchAsync("SaveAvatar", null).AsTask())).Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Authored_editor_mounts_real_controls_and_missing_bridge_stays_unavailable()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel(); using var host = new CuiSceneHost();
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, (await host.ShowAsync(StudioNativeScene.CreateUnavailable())).State);
            Assert.Empty(host.GetLogicalDescendants().OfType<Button>());
            foreach (var key in new[] { "Projects", "AvatarStates", "AvatarTransitions", "AvatarReactions" }) model.Set(key, Array.Empty<object>());
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await host.ShowAsync(StudioNativeScene.Create(model, model, new FixtureReady()))).State);
            Assert.DoesNotContain(host.Diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            Assert.Contains(host.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Save to Agent"));
            Assert.Contains(host.GetLogicalDescendants().OfType<CheckBox>(), c => Equals(c.Content, "Preview reduced motion"));
            return true;
        }, default);
    }
    private sealed class FixtureAssets : IAgentPresentationAssetAccess
    { public ValueTask<bool> CanReadAsync(string principal, string ns, string asset, CancellationToken ct) => ValueTask.FromResult(principal == "owner" && ns == "personal" && asset != "asset:denied"); }
    private sealed class FixtureReady : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "fixture", "Explicit test fixture")); }
}
public sealed class StudioTestApplication : Application { }

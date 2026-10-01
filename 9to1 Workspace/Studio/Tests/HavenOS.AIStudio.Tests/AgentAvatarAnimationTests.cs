using Avalonia.Controls;
using Avalonia.Headless;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

[Collection("StudioNative")]
public sealed class AgentAvatarAnimationTests
{
    private static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");

    [Fact]
    public async Task Nonloop_ends_at_real_decoder_boundary_and_reduced_motion_uses_static_fallback()
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var preview = fixture.Editor.Preview!;
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        Assert.True(preview.IsAnimating); Assert.Equal(80_000, preview.DelayMicroseconds); AssertRed(preview);
        Assert.True(await preview.AdvanceAsync()); Assert.Equal(120_000, preview.DelayMicroseconds); AssertBlue(preview);
        Assert.False(await preview.AdvanceAsync()); Assert.False(preview.IsAnimating); AssertBlue(preview);
        Assert.False(await preview.AdvanceAsync());
        fixture.Editor.Bindings.Set("ReducedMotion", true);
        Assert.Null(preview.Frame);
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        Assert.False(preview.IsAnimating); AssertRed(preview); Assert.False(await preview.AdvanceAsync());
    }

    [Fact]
    public async Task Each_loop_frame_rechecks_actual_den_acl_and_reference_revision()
    {
        await using var fixture = await Fixture.CreateAsync(true);
        var preview = fixture.Editor.Preview!;
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        await preview.AdvanceAsync(); AssertBlue(preview);
        await preview.AdvanceAsync(); AssertRed(preview); Assert.True(preview.IsAnimating);
        fixture.Policy.Allowed = false;
        await Assert.ThrowsAsync<DenException>(() => preview.AdvanceAsync());
        Assert.Null(preview.Frame); Assert.False(preview.IsAnimating);
        fixture.Policy.Allowed = true;
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        await fixture.Den.AddAttachmentAsync("personal", "animated", DenAgentPresentationAssets.AgentOwnerKind, "image/gif", Gif, "new-reference-revision");
        Assert.Equal(DenErrorCode.Conflict, (await Assert.ThrowsAsync<DenException>(() => preview.AdvanceAsync())).Code);
        Assert.Null(preview.Frame); Assert.False(preview.IsAnimating);
    }

    [Fact]
    public async Task Mounted_native_control_advances_timed_frames_then_stops_without_looping()
    {
        await using var fixture = await Fixture.CreateAsync(false);
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        var preview = fixture.Editor.Preview!;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        preview.Changed += (_, _) => { if (!preview.IsAnimating && preview.Frame is not null) ended.TrySetResult(); };
        await using var native = HeadlessUnitTestSession.StartNew(typeof(StudioTestApplication));
        await native.Dispatch(async () =>
        {
            using var control = new AgentAvatarPreviewControl(preview);
            var window = new Window { Content = control };
            window.Show();
            try
            {
                await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
                AssertBlue(preview); Assert.False(preview.IsAnimating); Assert.NotNull(control.Source);
                return true;
            }
            finally { window.Close(); }
        }, default);
        Assert.Null(preview.Frame);
    }

    [Fact]
    public async Task Cancelled_frame_retires_native_session_clears_presentation_and_can_reopen_authorized_asset()
    {
        await using var fixture = await Fixture.CreateAsync(true);
        var preview = fixture.Editor.Preview!;
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        AssertRed(preview);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.AdvanceAsync(cancelled.Token));
        Assert.Null(preview.Frame); Assert.False(preview.IsAnimating);
        await fixture.Editor.DispatchAsync("PreviewAvatar", null);
        AssertRed(preview); Assert.True(preview.IsAnimating);
        await Task.WhenAll(preview.DisposeAsync().AsTask(), preview.DisposeAsync().AsTask());
        Assert.Null(preview.Frame); Assert.False(preview.IsAnimating);
    }

    private static void AssertRed(AgentAvatarPreview preview) => AssertPixel(preview, [0, 0, 255, 255]);
    private static void AssertBlue(AgentAvatarPreview preview) => AssertPixel(preview, [255, 0, 0, 255]);
    private static void AssertPixel(AgentAvatarPreview preview, byte[] expected)
    {
        var pixels = preview.Frame!.CopyPixels();
        try { Assert.Equal(expected, pixels[..4]); }
        finally { Array.Clear(pixels); }
    }

    private sealed class Policy : IDenAccessPolicy
    {
        public bool Allowed = true;
        public ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Allowed && principal == "owner" && ns == "personal"); }
    }
    private sealed class Fixture(string root, DenStore store, DulcheDen den, Policy policy, AgentAvatarEditor editor) : IAsyncDisposable
    {
        public DulcheDen Den { get; } = den;
        public Policy Policy { get; } = policy;
        public AgentAvatarEditor Editor { get; } = editor;
        public static async Task<Fixture> CreateAsync(bool loop)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-studio-animation-" + Guid.NewGuid().ToString("N"));
            var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var policy = new Policy(); var den = new DulcheDen(store, policy, "owner");
            try
            {
                var agent = await den.SaveAsync(new AgentDefinitionRecord { Id = "animated", NamespaceId = "personal", DisplayName = "Animated", Version = "1" }, 0, "create");
                var attachment = await den.AddAttachmentAsync("personal", agent.Id, DenAgentPresentationAssets.AgentOwnerKind, "image/gif", Gif, "attach");
                var assets = new DenAgentPresentationAssets(den); var service = new AgentPresentationService(den, assets);
                await service.SetAsync("personal", agent.Id, agent.Revision,
                    new(1, AgentIconPresentation.Animated, attachment.Id, "Animated avatar", "idle", [new("idle", "Idle", attachment.Id, loop)], [], []), "presentation");
                var editor = new AgentAvatarEditor(service, assets); await editor.OpenAsync("personal", agent.Id);
                return new(root, store, den, policy, editor);
            }
            catch { await store.DisposeAsync(); Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await Editor.Preview!.DisposeAsync();
            await store.DisposeAsync();
            Directory.Delete(root, true);
        }
    }
}

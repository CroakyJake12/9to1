using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

/// <summary>Actual shared CUI content and native window adapters with genuine Home/Files.
/// Empty launch checks do not invoke or attest the Rnote/native installed-app admission.</summary>
[Collection("Canvas native UI")]
public sealed class CanvasBorrowedHomeLaunchTests
{
    [Fact]
    public async Task Window_presents_one_shared_Canvas_content_and_closes_only_its_borrowed_presentation()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var rig = await Rig.CreateAsync();
            var window = new CanvasHostWindow(rig.Home.Services, rig.Process.Token, null);
            window.Show();
            await window.Initialization;
            Assert.Same(window.OriginalSurface, window.Content);
            Assert.Same(window.Initialization, window.OriginalSurface.InitializeAsync());
            Assert.Single(window.GetVisualDescendants().OfType<CanvasHostSurface>());
            var close = window.CloseAndDrainAsync();
            Assert.Same(close, window.OriginalSurface.OriginalCloseTask);
            await close;
            Assert.Null(rig.Home.OriginalCloseTask);
            Assert.True(rig.Home.OriginalStartTask!.IsCompletedSuccessfully);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_same_content_mounts_without_constructing_an_offscreen_Canvas_window()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var rig = await Rig.CreateAsync();
            var surface = new CanvasHostSurface(rig.Home.Services, rig.Process.Token, null);
            var host = new Window { Content = surface, Width = 1200, Height = 800 };
            host.Show();
            try
            {
                var initialization = surface.InitializeAsync();
                Assert.Same(initialization, surface.InitializeAsync());
                await initialization;
                Assert.IsNotType<CanvasHostWindow>(host);
                Assert.Same(surface, host.Content);
                Assert.Contains(surface.GetVisualDescendants().OfType<Control>(), item =>
                    CuiRuntimeIdentity.GetStableId(item) == "id:canvas-launch" && item.IsEffectivelyVisible);
                Assert.DoesNotContain(surface.GetVisualDescendants().OfType<CanvasNativeViewport>(), item => item.IsEffectivelyVisible);
            }
            finally { await surface.CloseAndDrainAsync(); host.Close(); }
            Assert.Null(rig.Home.OriginalCloseTask);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Restored_context_in_a_real_dependency_callback_cannot_join_the_content_or_its_parent()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var rig = await Rig.CreateAsync();
            var early = ExecutionContext.Capture()!;
            var parent = new object();
            var provider = new ObservedProvider(rig.Home.Services);
            var surface = new CanvasHostSurface(provider, rig.Process.Token, null,
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () => { body(); return true; }));
            var observations = 0;
            provider.OnGet = type =>
            {
                if (type != typeof(HomeCoreRuntime)) return;
                ExecutionContext.Run(early.CreateCopy(), _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => surface.CloseAndDrainAsync());
                    Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(parent));
                    Assert.Null(surface.OriginalCloseTask);
                    observations++;
                }, null);
            };
            var host = new Window { Content = surface };
            host.Show();
            try { await surface.InitializeAsync(); Assert.True(observations > 0); }
            finally { provider.OnGet = null; await surface.CloseAndDrainAsync(); host.Close(); }
            Assert.Null(rig.Home.OriginalCloseTask);
            return true;
        }, CancellationToken.None);
    }

    private sealed class ObservedProvider(IServiceProvider original) : IServiceProvider
    {
        public Action<Type>? OnGet;
        public object? GetService(Type type) { OnGet?.Invoke(type); return original.GetService(type); }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-shared-home-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Process = new();
        internal HomeNativeWindowsComposition Home = null!;
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            try
            {
                NativeFilesWorkspaceService? files = null;
                rig.Home = new(new FileHomeCoreStateStore(Path.Combine(rig._root, "Home", "state.json")),
                    new OperatingSystemPrincipalSource(), new Paths(rig._root), new("canvas.shared.control." + Guid.NewGuid().ToString("N")),
                    originalActionPolicies: [new CanvasNativeActionPolicies()],
                    configureOriginalStores: identity =>
                    {
                        files = new(identity.StateStore, identity.Profiles);
                        return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                    },
                    configureOriginalResolvers: ownership =>
                    {
                        var authority = new NativeFilesWorkspaceAuthority(files!, ownership.Identity.Profiles, ownership.Ownership);
                        return new([], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = authority });
                    });
                await rig.Home.StartOriginalAsync();
                var empty = Path.Combine(rig._root, "explicit-empty-Files"); Directory.CreateDirectory(empty);
                await files!.ConfigureNewAsync(empty, rig.Home.LocalStoreOwnership, rig.Process.Token);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Process.Cancel();
            try { if (Home is not null) await Home.CloseAndDrainAsync(); }
            finally { Process.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}

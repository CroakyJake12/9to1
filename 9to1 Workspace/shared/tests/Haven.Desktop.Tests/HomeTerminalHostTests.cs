using System.Diagnostics;
using System.Text.Json;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Pages.Terminal;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Terminal;
using HavenOS.Apps.Terminal.NativeUI;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class HomeTerminalHostTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "The installed native Terminal viewport acceptance requires Linux.", SkipUnless = nameof(IsLinux))]
    public async Task Actual_host_mount_keeps_one_registered_PTY_and_retires_view_and_process_on_profile_revocation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await ui.Dispatch(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-terminal-host-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var principal = new Principal();
                var registrations = new ServiceCollection().AddHavenInfrastructure();
                registrations.AddSingleton<IAppPaths>(new Paths(root));
                registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
                registrations.AddSingleton<ITrustedHostPrincipalSource>(principal);
                registrations.AddSingleton<TerminalCommandActivityHub>();
                registrations.AddTerminalNativeActions();
                await using var services = registrations.BuildServiceProvider();
                var reviews = 0;
                using var page = await HomeTerminalPageFactory.OpenAsync(services, () => PermissionMode.FullAccess,
                    (_, _) => { reviews++; return Task.CompletedTask; }, root, token);
                var window = new Window { Content = page, Width = 1000, Height = 900 };
                window.Show();
                try
                {
                    Assert.Equal(CuiSceneAvailabilityState.Ready, Assert.IsType<CuiSceneHost>(page.Content).Availability!.State);
                    var viewport = Assert.Single(page.GetVisualDescendants().OfType<LibVTermViewport>());
                    var registry = services.GetRequiredService<TerminalOwnedSessionRegistry>();
                    var firstId = page.SessionMetadata!.SessionId;
                    using var persisted = JsonDocument.Parse(MainView.CreateDetachedTabSnapshot(
                        new WorkspaceTabViewModel("terminal", "Terminal", page, true, HavenSurface.Terminal)).StateJson);
                    Assert.Equal(firstId, persisted.RootElement.GetProperty("TerminalSessionId").GetGuid());
                    Assert.Equal(page.SessionMetadata.CurrentWorkingDirectory,
                        persisted.RootElement.GetProperty("TerminalCurrentWorkingDirectory").GetString());
                    var first = await registry.GetAsync(firstId, token);
                    Assert.True(first.ProcessID > 0);
                    using var firstProcess = Process.GetProcessById(first.ProcessID);
                    var input = Assert.Single(page.GetVisualDescendants().OfType<TextBox>());
                    input.Text = "printf '\\033[31mhost-%s\\033[0m\\n' 'proof'";
                    Click(page, "Submit");
                    await Eventually(() => Task.FromResult(viewport.ScreenSnapshot is { } frame &&
                        string.Concat(frame.Cells.Select(cell => cell.Text)).Contains("host-proof", StringComparison.Ordinal)), token);
                    Click(page, "New session");
                    await Eventually(async () =>
                    {
                        if (page.SessionMetadata?.SessionId is not { } id || id == firstId) return false;
                        try { return (await registry.GetAsync(id, token)).ProcessID > 0; }
                        catch (UnauthorizedAccessException) { return false; }
                    }, token);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.GetAsync(firstId, token));
                    await Eventually(() => Task.FromResult(firstProcess.HasExited), token);
                    var secondId = page.SessionMetadata!.SessionId;
                    using var secondProcess = Process.GetProcessById((await registry.GetAsync(secondId, token)).ProcessID);
                    principal.Revoked = true;
                    await page.ActivateAsync(token);
                    Assert.Null(page.Content);
                    Assert.Null(viewport.ScreenSnapshot);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.GetAsync(secondId, token));
                    await Eventually(() => Task.FromResult(secondProcess.HasExited), token);
                    Assert.Equal(0, reviews);
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(root, true); }
        }, token);
    }

    [Fact]
    public async Task Unavailable_restored_terminal_preserves_exact_snapshot_and_never_mounts_viewport()
    {
        var token = TestContext.Current.CancellationToken;
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await ui.Dispatch(async () =>
        {
            const string original = "{\"Key\":\"terminal\",\"TerminalSessionId\":\"3e324b3f-5cac-43b2-ac8a-bffef874c20a\",\"unknown\":{\"retain\":true}}";
            using var page = new UnavailableTerminalPage(original);
            await page.InitializeAsync("The installed native viewport is unavailable.", token);
            var window = new Window { Content = page, Width = 800, Height = 600 };
            window.Show();
            try
            {
                Assert.Equal(CuiSceneAvailabilityState.Unavailable, Assert.IsType<CuiSceneHost>(page.Content).Availability!.State);
                Assert.Empty(page.GetVisualDescendants().OfType<LibVTermViewport>());
                var saved = MainView.CreateDetachedTabSnapshot(new WorkspaceTabViewModel("terminal", "Terminal", page, true, HavenSurface.Terminal));
                Assert.Equal(original, saved.StateJson);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "The installed native viewport is unavailable.");
            }
            finally { window.Close(); }
        }, token);
    }

    private static void Click(Control control, string caption)
    {
        var button = Assert.Single(control.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, caption));
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Eventually(Func<Task<bool>> condition, CancellationToken token)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(20, token);
        }
        Assert.Fail("The actual terminal host did not reach its expected process or presentation state.");
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public bool Revoked;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Revoked
            ? ValueTask.FromResult<string?>(null) : new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}

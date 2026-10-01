using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure.Terminal;
using HavenOS.Apps.Terminal.NativeUI;
using Xunit;

namespace HavenOS.Apps.Terminal.NativeUI.Tests;

public sealed class TerminalCuiWorkspaceTests
{
    [Fact]
    public async Task Real_session_mount_routes_input_and_denies_ai_permission_and_replaced_session()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This acceptance requires the actual Linux PTY adapter.");
        var root = Path.Combine(Path.GetTempPath(), "astra-terminal-cui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TerminalEnvironmentDescriptor(new("native-ui-local"), "native-ui-pty", TerminalEnvironmentKind.LocalHost,
                "Native UI fixture", TerminalEnvironmentConnectionState.Ready, "linux", "x64",
                TerminalEnvironmentCapability.InteractivePty | TerminalEnvironmentCapability.Resize | TerminalEnvironmentCapability.Signals);
            var factory = new PtyTerminalSessionFactory(environment, new UnixPtyProcessFactory(),
                [new("native-ui-shell", "Fixture shell", "/bin/sh", ["-c", "exec sleep 30"], "native-ui-pty", false, TerminalEnvironmentConnectionState.Ready)]);
            var permission = PermissionMode.FullAccess;
            using var surface = new TerminalAppSurface(new(factory, () => permission), root);
            Assert.NotNull(surface.InteractiveSession);
            await using var native = HeadlessUnitTestSession.StartNew(typeof(TerminalUiTestApplication));
            await native.Dispatch(async () =>
            {
                var probe = new ViewportBoundaryProbe(); // Boundary observer, not a claimed terminal emulator.
                using var workspace = new TerminalCuiWorkspace(surface, probe);
                var scene = TerminalNativeScene.Create(workspace, new ReadyFixture());
                using var host = new CuiSceneHost(scene.ControlRegistry);
                await host.ShowAsync(scene);
                Assert.Contains(probe.View, host.GetLogicalDescendants());
                Assert.Same(surface.InteractiveSession, probe.Session);
                Assert.True(probe.InputEnabled);
                var firstInput = probe.Input!;
                var echo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var output = new System.Text.StringBuilder();
                surface.OutputReceived += (_, chunk) =>
                {
                    lock (output)
                    {
                        output.Append(chunk.Text);
                        if (output.ToString().Contains("native-ui-input", StringComparison.Ordinal)) echo.TrySetResult();
                    }
                };
                await firstInput(System.Text.Encoding.UTF8.GetBytes("native-ui-input\n"), default);
                await echo.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await workspace.DispatchAsync("TerminalAiMode", null);
                Assert.False(probe.InputEnabled);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => firstInput(new byte[] { 65 }, default).AsTask());
                await workspace.DispatchAsync("TerminalCommandMode", null);
                permission = PermissionMode.Ask;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => firstInput(new byte[] { 65 }, default).AsTask());
                var commandInput = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>());
                commandInput.Text = "printf displayed-review";
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal("printf displayed-review", workspace.Bindings.Get("InputText"));
                await workspace.DispatchAsync("TerminalSubmit", null);
                var displayed = surface.PendingCommandId;
                Assert.NotNull(displayed);
                Assert.Equal("printf displayed-review", workspace.Bindings.Get("PendingCommandPreview"));
                // Replace the owner's pending command without repainting the scene. The
                // old displayed approval must not approve the replacement command.
                var replacement = await surface.SubmitAsync("printf replacement-review");
                Assert.Equal(TerminalAppCommandState.RequiresApproval, replacement.State);
                var replacementId = surface.PendingCommandId;
                Assert.NotEqual(displayed, replacementId);
                await workspace.DispatchAsync("TerminalApproveCommand", null);
                Assert.Equal(replacementId, surface.PendingCommandId);
                Assert.Equal("The displayed command review changed.", workspace.Bindings.Get("Status"));
                Assert.Equal("printf replacement-review", workspace.Bindings.Get("PendingCommandPreview"));
                await workspace.DispatchAsync("TerminalDenyCommand", null);
                Assert.Null(surface.PendingCommandId);
                permission = PermissionMode.FullAccess;
                var firstSession = probe.Session;
                await workspace.DispatchAsync("TerminalNewSession", null);
                Assert.NotSame(firstSession, probe.Session);
                Assert.Same(surface.InteractiveSession, probe.Session);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => firstInput(new byte[] { 65 }, default).AsTask());
                var lastInput = probe.Input!;
                workspace.Dispose();
                Assert.Null(probe.Session);
                Assert.False(probe.InputEnabled);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lastInput(new byte[] { 65 }, default).AsTask());
                return true;
            }, default);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_pty_ansi_stream_renders_through_owning_native_viewport()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-terminal-screen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TerminalEnvironmentDescriptor(new("native-screen-local"), "native-screen-pty", TerminalEnvironmentKind.LocalHost,
                "Native screen fixture", TerminalEnvironmentConnectionState.Ready, "linux", "x64",
                TerminalEnvironmentCapability.InteractivePty | TerminalEnvironmentCapability.Resize | TerminalEnvironmentCapability.Signals);
            var factory = new PtyTerminalSessionFactory(environment, new UnixPtyProcessFactory(),
                [new("native-screen-shell", "Fixture shell", "/bin/sh", ["-i"], "native-screen-pty", false, TerminalEnvironmentConnectionState.Ready)]);
            using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess), root);
            await using var native = HeadlessUnitTestSession.StartNew(typeof(TerminalUiTestApplication));
            await native.Dispatch(async () =>
            {
                using var viewport = new LibVTermViewport();
                using var workspace = new TerminalCuiWorkspace(surface, viewport);
                var scene = TerminalNativeScene.Create(workspace, new ReadyFixture());
                using var host = new CuiSceneHost(scene.ControlRegistry);
                await host.ShowAsync(scene);
                var window = new Window { Width = 900, Height = 700, Content = host };
                window.Show();
                try
                {
                    var session = Assert.IsAssignableFrom<ITerminalInteractiveSession>(surface.InteractiveSession);
                    Assert.True(viewport.Focus());
                    window.KeyTextInput("printf '\\033[2J\\033[H\\033[32mNative\\033[0m'");
                    window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
                        Avalonia.Input.PhysicalKey.Enter, "\r");
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (viewport.ScreenSnapshot?.Cells[0].Text != "N" && DateTime.UtcNow < deadline) await Task.Delay(20);
                    Assert.Null(viewport.Failure);
                    var frame = Assert.IsType<TerminalScreenSnapshot>(viewport.ScreenSnapshot);
                    Assert.Equal("Native", string.Concat(frame.Cells.Take(6).Select(cell => cell.Text)));
                    Assert.NotEqual(frame.Cells[0].Foreground, frame.Cells[6].Foreground);
                    using var rendered = window.CaptureRenderedFrame();
                    Assert.NotNull(rendered);
                    if (Environment.GetEnvironmentVariable("ASTRA_TERMINAL_SCREENSHOT") is { Length: > 0 } screenshot)
                        rendered.Save(screenshot);
                    Assert.True(viewport.Bounds.Height >= 240);
                    await workspace.DispatchAsync("TerminalAiMode", null);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => surface.SendInteractiveInputAsync(session.Metadata.SessionId, new byte[] {65}, default).AsTask());
                }
                finally { window.Close(); }
                return true;
            }, default);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Output_flood_fails_once_with_bounded_chunks_and_new_attachment_recovers()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(TerminalUiTestApplication));
        await native.Dispatch(async () =>
        {
            using var viewport = new LibVTermViewport();
            var noisy = new ControlledOutputSession();
            viewport.Attach(noisy, (_, _) => throw new InvalidOperationException("An output fixture must not send input."));
            // Deliberately keep the UI thread busy while a provider emits many tiny
            // chunks: a byte-only limit would still allow an unbounded callback load.
            for (var i = 0; i < 100_000; i++) noisy.Emit(new byte[] { 65 });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal("Terminal output exceeded the bounded render queue.", viewport.Failure);
            var fresh = new ControlledOutputSession();
            viewport.Attach(fresh, (_, _) => throw new InvalidOperationException("An output fixture must not send input."));
            noisy.Emit(new byte[] { 90 });
            fresh.Emit(new byte[] { 70 });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Null(viewport.Failure);
            Assert.Equal("F", viewport.ScreenSnapshot!.Cells[0].Text);
            fresh.Emit(new byte[] { 88 }); // queued for the old attachment
            viewport.Attach(fresh, (_, _) => throw new InvalidOperationException("An output fixture must not send input."));
            fresh.Emit(new byte[] { 78 });
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var replacementFrame = Assert.IsType<TerminalScreenSnapshot>(viewport.ScreenSnapshot);
            Assert.Equal("N", replacementFrame.Cells[0].Text);
            Assert.Empty(replacementFrame.Cells[1].Text);
            return true;
        }, default);
    }

    // Controlled byte producer for queue-boundary testing only; no process or
    // action authority is represented by this fixture.
    private sealed class ControlledOutputSession : ITerminalInteractiveSession
    {
        public TerminalSessionMetadata Metadata { get; } = new(Guid.NewGuid(), "fixture", "Controlled output", "/", "/",
            TerminalSessionLifecycleState.Ready, DateTimeOffset.UtcNow, 0, false);
        public int? ProcessId => null;
        public TerminalEnvironmentDescriptor Environment => throw new NotSupportedException();
        public TerminalSessionMode Mode => TerminalSessionMode.Command;
        public event EventHandler<TerminalSessionOutput>? OutputReceived;
        public event EventHandler<TerminalSessionMetadata>? MetadataChanged { add { } remove { } }
        public void Emit(byte[] bytes) => OutputReceived?.Invoke(this, new(Metadata.SessionId, null,
            TerminalOutputStream.StandardOutput, "", DateTimeOffset.UtcNow) { RawBytes = bytes });
        public Task<TerminalSessionCommandResult> ExecuteAsync(Guid id, string command, CancellationToken ct) => throw new NotSupportedException();
        public Task InterruptAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task SetWorkingDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task SetModeAsync(TerminalSessionMode mode, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask ResizeAsync(ushort columns, ushort rows, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SignalAsync(TerminalProcessSignal signal, CancellationToken ct = default) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ViewportBoundaryProbe : ITerminalNativeViewport
    {
        public Control View { get; } = new Border();
        public ITerminalInteractiveSession? Session;
        public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? Input;
        public bool InputEnabled;
        public void Attach(ITerminalInteractiveSession session, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendInput)
        { Session = session; Input = sendInput; }
        public void Detach() { Session = null; Input = null; }
        public void SetInteractiveInputEnabled(bool enabled) => InputEnabled = enabled;
    }
    private sealed class ReadyFixture : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "fixture", "Actual controlled fixture session"));
    }
}
public sealed class TerminalUiTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<TerminalUiTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
}

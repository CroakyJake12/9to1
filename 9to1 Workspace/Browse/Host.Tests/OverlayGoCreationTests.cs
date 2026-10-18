using System.Reflection;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;

namespace HavenOS.Browse.Host.Tests;

public sealed class OverlayGoCreationTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [AvaloniaFact]
    public async Task Failed_catalogues_join_held_siblings_and_retire_the_actual_unpublished_page()
    {
        using var rig = new Rig();
        var first = new IOException("controlled catalogue failure");
        var sibling = new OperationCanceledException("faulted original, not canceled work");
        var other = new InvalidOperationException("another original failed");
        var creating = rig.Factory.CreateAsync(CancellationToken.None);
        try
        {
            Assert.NotNull(rig.Page);
            Assert.Equal(4, rig.Acquired);
            rig.Agents.SetException([first, sibling]);
            rig.Capabilities.SetResult([]);
            rig.Prompts.SetException(other);
            Assert.False(creating.IsCompleted);
            Assert.False(rig.Disposed);
            rig.Apps.SetResult([]);
            var failure = await Record.ExceptionAsync(() => creating.WaitAsync(Bound));
            Assert.NotNull(failure);
            Assert.True(creating.IsFaulted);
            Assert.False(creating.IsCanceled);
            Assert.Contains(Causes(failure), x => ReferenceEquals(first, x));
            Assert.Contains(Causes(failure), x => ReferenceEquals(sibling, x));
            Assert.Contains(Causes(failure), x => ReferenceEquals(other, x));
            Assert.True(rig.Disposed);
            Assert.True(rig.Page!.OriginalClose!.IsCompletedSuccessfully);
            Assert.Null(rig.Page.SceneHost.Root);
        }
        finally { await rig.CleanupAsync(creating); }
    }

    [AvaloniaFact]
    public async Task Synchronous_later_acquisition_failure_joins_every_earlier_original_and_preserves_cleanup_failure()
    {
        using var rig = new Rig();
        var acquisition = new OperationCanceledException("no canceled original task was supplied");
        var cleanup = new InvalidOperationException("controlled disposal observer failure");
        rig.SynchronousPromptFailure = acquisition;
        rig.CleanupFailure = cleanup;
        var creating = rig.Factory.CreateAsync(CancellationToken.None);
        try
        {
            Assert.Equal(3, rig.Acquired); // agents, capabilities, then failed prompts; no apps acquired.
            Assert.False(creating.IsCompleted);
            Assert.False(rig.Disposed);
            rig.Capabilities.SetResult([]);
            Assert.False(creating.IsCompleted); // The first original is still held.
            rig.Agents.SetResult([]);
            var failure = await Record.ExceptionAsync(() => creating.WaitAsync(Bound));
            Assert.NotNull(failure);
            Assert.True(creating.IsFaulted);
            Assert.Contains(Causes(failure), x => ReferenceEquals(acquisition, x));
            Assert.Contains(Causes(failure), x => ReferenceEquals(cleanup, x));
            Assert.True(rig.Disposed);
            Assert.True(rig.Page!.OriginalClose!.IsFaulted);
            Assert.Null(rig.Page.SceneHost.Root);
        }
        finally { await rig.CleanupAsync(creating); }
    }

    [AvaloniaFact]
    public async Task Caller_cancellation_does_not_publish_or_close_while_an_original_is_held()
    {
        using var rig = new Rig();
        using var stop = new CancellationTokenSource();
        var creating = rig.Factory.CreateAsync(stop.Token);
        try
        {
            Assert.Equal(4, rig.Acquired);
            stop.Cancel();
            Assert.False(creating.IsCompleted);
            Assert.False(rig.Disposed);
            rig.Release(); // Real sources acknowledge terminal results only now.
            var failure = await Record.ExceptionAsync(() => creating.WaitAsync(Bound));
            Assert.NotNull(failure);
            Assert.Contains(Causes(failure), x => x is OperationCanceledException canceled && canceled.CancellationToken == stop.Token);
            Assert.True(rig.Disposed);
            Assert.Null(rig.Page!.SceneHost.Root);
        }
        finally { await rig.CleanupAsync(creating); }
    }

    [AvaloniaFact]
    public async Task Successful_actual_page_is_returned_live_using_the_current_host_catalogue()
    {
        using var rig = new Rig();
        var creating = rig.Factory.CreateAsync(CancellationToken.None);
        try
        {
            rig.Release();
            var page = await creating.WaitAsync(Bound);
            Assert.Same(rig.Page, page);
            Assert.False(rig.Disposed);
            Assert.NotNull(page.SceneHost.Root);
            Assert.Equal(OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Linux, rig.Dynamic.Platform);
            await page.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(rig.Disposed);
            Assert.Null(page.SceneHost.Root);
        }
        finally { await rig.CleanupAsync(creating); }
    }

    private static IEnumerable<Exception> Causes(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var item in Causes(child)) yield return item;
        else if (cause.InnerException is { } inner)
            foreach (var item in Causes(inner)) yield return item;
    }

    private sealed class Rig : IDisposable
    {
        public readonly TaskCompletionSource<IReadOnlyList<AgentDefinition>> Agents = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<IReadOnlyList<CapabilityDefinition>> Capabilities = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<IReadOnlyList<PromptDefinition>> Prompts = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<IReadOnlyList<ModeDefinition>> Apps = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly RecordingProvider Dynamic = new();
        public GoPage? Page;
        public bool Disposed;
        public int Acquired;
        public Exception? SynchronousPromptFailure;
        public Exception? CleanupFailure;
        public OverlayGoSessionFactory Factory { get; }
        private readonly string _directory = Directory.CreateTempSubdirectory("9to1-go-creation-").FullName;
        public Rig()
        {
            var bus = new HavenEventBus();
            var catalog = Stub<ICatalogRepository>((name, _) =>
            {
                ++Acquired;
                return name switch
                {
                    "GetAgentsAsync" => Agents.Task,
                    "GetPromptsAsync" => SynchronousPromptFailure is { } failure ? throw failure : Prompts.Task,
                    _ => throw Unexpected(name)
                };
            });
            var capabilities = new CapabilityRegistryService(Stub<ICapabilityRepository>((name, _) =>
            {
                if (name != "GetCapabilitiesAsync") throw Unexpected(name);
                ++Acquired; return Capabilities.Task;
            }), [Dynamic]);
            var modes = Stub<IModeRegistry>((name, _) =>
            {
                if (name != "GetModesAsync") throw Unexpected(name);
                ++Acquired; return Apps.Task;
            });
            Factory = new(bus, catalog, capabilities, modes,
                Stub<IConversationRepository>((name, _) => throw Unexpected(name)),
                Stub<IOllamaClient>((name, _) => throw Unexpected(name)),
                new UserPreferencesService(new Paths(_directory)), () =>
                {
                    Page = new GoPage(bus); // Actual production page/route/lifetime; only observe its acquisition.
                    Page.Disposed += (_, _) => { Disposed = true; if (CleanupFailure is { } failure) throw failure; };
                    return Page;
                });
        }
        public void Release() { Agents.TrySetResult([]); Capabilities.TrySetResult([]); Prompts.TrySetResult([]); Apps.TrySetResult([]); }
        public async Task CleanupAsync(Task<GoPage> creating)
        {
            Release();
            try { await creating.WaitAsync(Bound); }
            catch (Exception failure) when (failure is not TimeoutException) { /* The original creation result is asserted by each case. */ }
            if (Page is null) return;
            try { await Page.CloseAndDrainAsync().WaitAsync(Bound); }
            catch (Exception failure) when (CleanupFailure is { } expected && Causes(failure).Any(cause => ReferenceEquals(cause, expected))) { }
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
    private sealed class RecordingProvider : IDynamicCapabilityProvider
    {
        public CapabilityPlatform? Platform;
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CapabilityPlatform platform, CancellationToken cancellationToken)
        { Platform = platform; return Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]); }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
    }
    public class ControlledIo : DispatchProxy
    {
        internal Func<string, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    { var proxy = DispatchProxy.Create<T, ControlledIo>(); ((ControlledIo)(object)proxy).Call = call; return proxy; }
    private static InvalidOperationException Unexpected(string name) => new("Unexpected controlled I/O: " + name);
}

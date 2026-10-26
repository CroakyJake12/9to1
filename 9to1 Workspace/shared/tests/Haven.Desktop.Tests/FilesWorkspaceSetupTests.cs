using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Files;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual Linux OS-local Home, ownership and Files stores with headless CUI.
/// The injected picker completion is not evidence that a native OS dialog ran.</summary>
public sealed class FilesWorkspaceSetupTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    public static bool UsesLinuxFilesSetupFixture => OperatingSystem.IsLinux();

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture.")]
    public async Task Retired_origin_does_not_resume_after_later_original_Files_setup()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var session = rig.Create(_ => Task.FromResult<string?>(rig.Empty));
        await session.InitializeAsync().WaitAsync(Limit);
        var setup = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
        using var origin = new CancellationTokenSource(); var resumed = 0;
        var continuation = session.ContinueAfterOriginalWorkspaceSetupAsync(() => { resumed++; return Task.CompletedTask; }, origin.Token);
        origin.Cancel(); await continuation.WaitAsync(Limit);
        await setup.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit);
        Assert.Equal(0, resumed);
        Assert.NotNull(await rig.Files.GetConfigurationAsync());
        Assert.True(await session.TryPrepareToCloseAsync().WaitAsync(Limit));
        await session.CloseAndDrainAsync().WaitAsync(Limit);
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task First_use_cancel_and_nonempty_refusal_preserve_state_then_explicit_empty_choice_opens_real_Files()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        string? selection = null;
        var session = rig.Create(_ => Task.FromResult(selection));
        var window = new Window { Width = 900, Height = 600 };
        try
        {
            await session.InitializeAsync().WaitAsync(Limit);
            var setup = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
            Assert.True(session.IsPublicationCurrent); Assert.Equal("9to1://files", session.Address.ToString());
            Assert.Null(await rig.Files.GetConfigurationAsync());
            window.Content = setup; window.Show(); window.UpdateLayout();
            var resumed = 0;
            using var origin = new CancellationTokenSource();
            var continuation = session.ContinueAfterOriginalWorkspaceSetupAsync(() => { resumed++; return Task.CompletedTask; }, origin.Token);
            var choose = Assert.Single(setup.GetVisualDescendants().OfType<Button>());
            Assert.Equal("Choose an empty folder", choose.Content);
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await setup.WhenActionsIdleAsync().WaitAsync(Limit);
            Assert.Contains("No folder selected", setup.Status);
            Assert.False(continuation.IsCompleted); Assert.Equal(0, resumed);
            Assert.Null(await rig.Files.GetConfigurationAsync());

            selection = rig.Nonempty; var original = await File.ReadAllBytesAsync(rig.PreservedFile);
            await setup.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit);
            Assert.Contains("existing empty local folder", setup.Status);
            Assert.False(continuation.IsCompleted); Assert.Equal(0, resumed);
            Assert.Equal(original, await File.ReadAllBytesAsync(rig.PreservedFile));
            Assert.Single(Directory.EnumerateFileSystemEntries(rig.Nonempty));
            Assert.Null(await rig.Files.GetConfigurationAsync());

            selection = rig.Empty;
            await setup.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit);
            await continuation.WaitAsync(Limit); Assert.Equal(1, resumed);
            var configuration = Assert.IsType<NativeFilesWorkspaceConfiguration>(await rig.Files.GetConfigurationAsync());
            Assert.Equal(rig.Empty, configuration.RootDirectory);
            var binding = await rig.Home.LocalStoreOwnership.GetVerifiedAsync("files", configuration.StoreId.ToString("D"));
            Assert.NotNull(binding); Assert.Equal(rig.Profile, binding.ProfileId);
            Assert.IsType<FilesNativeBrowserSurface>(setup.Content);
            Assert.Same(setup, session.OriginalNativeView); Assert.True(session.IsPublicationCurrent);
            Assert.Equal(FilesBrowseAppContentAddress.For(configuration.StoreId, null), session.Address);
            Assert.True(Directory.Exists(Path.Combine(rig.Empty, ".9to1-files")));
            Assert.True(await session.TryPrepareToCloseAsync().WaitAsync(Limit));
            await session.CloseAndDrainAsync().WaitAsync(Limit);
            Assert.True(session.CanRetireOriginalView); Assert.Null(rig.Home.OriginalCloseTask);
            Assert.True(setup.OriginalClose!.IsCompletedSuccessfully); Assert.Null(setup.Content);
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await setup.WhenActionsIdleAsync().WaitAsync(Limit);
            Assert.True(setup.OriginalClose.IsCompletedSuccessfully);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Existing_configuration_keeps_real_distinct_views_and_exact_store_without_setup_or_replacement()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var configured = await rig.Files.ConfigureNewAsync(rig.Empty, rig.Home.LocalStoreOwnership, TestContext.Current.CancellationToken);
        var factory = FilesBrowseAppContentFactory.BindOriginalSameProcess(rig.Provider, rig.Home,
            rig.App.Token, rig.Window.Token);
        var first = rig.Track(factory.CreateSession(rig.Request()));
        var second = rig.Track(factory.CreateSession(rig.Request()));
        await first.InitializeAsync().WaitAsync(Limit); await second.InitializeAsync().WaitAsync(Limit);
        Assert.IsType<FilesNativeBrowserSurface>(first.OriginalNativeView);
        Assert.IsType<FilesNativeBrowserSurface>(second.OriginalNativeView);
        Assert.NotSame(first.OriginalNativeView, second.OriginalNativeView);
        Assert.Equal(configured.Configuration.StoreId, (await rig.Files.GetConfigurationAsync())!.StoreId);
        Assert.Equal(first.Address, second.Address);
        await first.CloseAndDrainAsync().WaitAsync(Limit);
        Assert.True(second.IsPublicationCurrent); Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Failed_Files_retirement_keeps_real_child_after_setup_loader_settles()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var session = rig.Create(_ => Task.FromResult<string?>(rig.Empty));
        await session.InitializeAsync().WaitAsync(Limit);
        var root = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
        await root.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit);
        var actualView = Assert.IsType<FilesNativeBrowserSurface>(root.Content);
        // Move only this disposable fixture's store. The real source now reports
        // its configured directory unavailable; no fake Files service is substituted.
        Directory.Move(rig.Empty, rig.Empty + "-retained");
        Assert.NotNull(await Record.ExceptionAsync(() => actualView.RefreshAsync(CancellationToken.None).WaitAsync(Limit)));
        rig.ExpectFailedClose(session);
        Assert.NotNull(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Limit)));
        Assert.True(root.OriginalClose!.IsCompletedSuccessfully); // Setup loader itself settled.
        Assert.Same(actualView, root.Content); // Files failure prevents child release.
        Assert.False(session.CanRetireOriginalView); Assert.False(session.IsPublicationCurrent);
        Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Failed_configured_view_publication_is_retained_without_replacing_or_rolling_back_canonical_store()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var session = rig.Create(_ => Task.FromResult<string?>(rig.Empty));
        await session.InitializeAsync().WaitAsync(Limit);
        var originalRoot = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
        var actualFailure = new IOException("Actual publication observer refused the configured Files view.");
        session.StateChanged += (_, _) => throw actualFailure;
        var failure = await Record.ExceptionAsync(() => originalRoot.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit));
        Assert.Same(actualFailure, failure); rig.ExpectFailedClose(session);
        Assert.False(session.IsPublicationCurrent);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var configuration = (await rig.Files.GetConfigurationAsync())!;
        Assert.NotNull(await rig.Home.LocalStoreOwnership.GetVerifiedAsync("files", configuration.StoreId.ToString("D")));
        Assert.NotNull(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Limit)));
        Assert.False(session.CanRetireOriginalView); Assert.True(originalRoot.OriginalClose!.IsCompleted);
        Assert.Null(rig.Home.OriginalCloseTask);
        var factory = FilesBrowseAppContentFactory.BindOriginalSameProcess(rig.Provider, rig.Home, rig.App.Token, rig.Window.Token);
        var reopened = rig.Track(factory.CreateSession(rig.Request()));
        await reopened.InitializeAsync().WaitAsync(Limit);
        Assert.IsType<FilesNativeBrowserSurface>(reopened.OriginalNativeView);
        Assert.Equal(FilesBrowseAppContentAddress.For(configuration.StoreId, null), reopened.Address);
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Another_tab_finishing_setup_during_picker_preserves_its_store_and_does_not_adopt_second_choice()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = rig.Create(_ => Task.FromResult<string?>(rig.Empty));
        var second = rig.Create(_ => { entered.TrySetResult(); return released.Task; });
        Task? secondChoice = null;
        var unused = Path.Combine(Path.GetDirectoryName(rig.Empty)!, "unused"); Directory.CreateDirectory(unused);
        try
        {
            await first.InitializeAsync().WaitAsync(Limit); await second.InitializeAsync().WaitAsync(Limit);
            var firstRoot = Assert.IsType<FilesWorkspaceSetupPage>(first.OriginalNativeView);
            var secondRoot = Assert.IsType<FilesWorkspaceSetupPage>(second.OriginalNativeView);
            secondChoice = secondRoot.DispatchAsync("files.setup.choose", null).AsTask();
            await entered.Task.WaitAsync(Limit);
            await firstRoot.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit);
            var firstConfiguration = (await rig.Files.GetConfigurationAsync())!;
            released.TrySetResult(unused); await secondChoice.WaitAsync(Limit);
            var secondConfiguration = (await rig.Files.GetConfigurationAsync())!;
            Assert.Equal(firstConfiguration.StoreId, secondConfiguration.StoreId);
            Assert.Equal(rig.Empty, secondConfiguration.RootDirectory); Assert.Empty(Directory.EnumerateFileSystemEntries(unused));
            Assert.IsType<FilesNativeBrowserSurface>(firstRoot.Content); Assert.IsType<FilesNativeBrowserSurface>(secondRoot.Content);
            Assert.NotSame(firstRoot.Content, secondRoot.Content);
            Assert.Same(firstRoot, first.OriginalNativeView); Assert.Same(secondRoot, second.OriginalNativeView);
            Assert.Equal(first.Address, second.Address);
        }
        finally
        {
            released.TrySetResult(null);
            if (secondChoice is not null) await secondChoice.WaitAsync(Limit);
        }
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Wrong_profile_and_unconfigured_typed_store_do_not_offer_setup_or_create_a_different_store()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var foreign = rig.Create(_ => throw new InvalidOperationException("Picker must not run."),
            rig.Request() with { ProfileId = Guid.NewGuid().ToString("D") });
        var typed = rig.Create(_ => throw new InvalidOperationException("Picker must not run."),
            rig.Request() with { Address = FilesBrowseAppContentAddress.For(Guid.NewGuid(), null) });
        foreach (var session in new[] { foreign, typed })
        {
            Assert.NotNull(await Record.ExceptionAsync(() => session.InitializeAsync().WaitAsync(Limit)));
            Assert.False(session.IsPublicationCurrent);
            Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
            rig.ExpectFailedClose(session);
            Assert.NotNull(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Limit)));
        }
        Assert.Null(await rig.Files.GetConfigurationAsync()); Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Empty));
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Close_joins_the_actual_held_picker_and_never_creates_after_retirement()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = rig.Create(_ => { entered.TrySetResult(); return released.Task; });
        Task? choice = null, close = null;
        var window = new Window();
        try
        {
            await session.InitializeAsync().WaitAsync(Limit);
            var page = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
            window.Content = page; window.Show(); window.UpdateLayout();
            var choose = Assert.Single(page.GetVisualDescendants().OfType<Button>());
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            choice = page.WhenActionsIdleAsync();
            await entered.Task.WaitAsync(Limit);
            Assert.False(await session.TryPrepareToCloseAsync().WaitAsync(Limit));
            close = session.CloseAndDrainAsync(); rig.ExpectFailedClose(session);
            Assert.Same(close, session.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
            Assert.False(choice.IsCompleted); Assert.Null(rig.Home.OriginalCloseTask);
            released.TrySetResult(rig.Empty);
            Assert.NotNull(await Record.ExceptionAsync(() => choice.WaitAsync(Limit)));
            Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(Limit)));
            Assert.True(choice.IsCompleted); Assert.True(close.IsCompleted);
            Assert.NotNull(page.OriginalClose); Assert.True(page.OriginalClose.IsCompleted);
            Assert.False(page.OriginalClose.IsCompletedSuccessfully);
            Assert.False(session.CanRetireOriginalView); Assert.False(session.IsPublicationCurrent);
            Assert.Null(await rig.Files.GetConfigurationAsync()); Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Empty));
        }
        finally
        {
            released.TrySetResult(null);
            if (choice is not null) _ = await Record.ExceptionAsync(() => choice.WaitAsync(Limit));
            if (close is not null) _ = await Record.ExceptionAsync(() => close.WaitAsync(Limit));
            window.Content = null; window.Close();
        }
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxFilesSetupFixture),
        Skip = "Actual Linux Home/Files custody fixture; native Windows and macOS acceptance is separate.")]
    public async Task Changed_OS_principal_after_folder_choice_is_refused_before_configuration()
    {
        await using var rig = new Rig(); await rig.StartAsync();
        var session = rig.Create(_ => { rig.Principal.Refuse = true; return Task.FromResult<string?>(rig.Empty); });
        await session.InitializeAsync().WaitAsync(Limit);
        var page = Assert.IsType<FilesWorkspaceSetupPage>(session.OriginalNativeView);
        try
        {
            Assert.NotNull(await Record.ExceptionAsync(() => page.DispatchAsync("files.setup.choose", null).AsTask().WaitAsync(Limit)));
            Assert.Contains("could not finish", page.Status);
            rig.ExpectFailedClose(session);
            Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Empty));
        }
        finally { rig.Principal.Refuse = false; }
        Assert.Null(await rig.Files.GetConfigurationAsync());
        Assert.NotNull(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Limit)));
    }

    private sealed class PrincipalSource : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal bool Refuse;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Refuse
            ? ValueTask.FromResult<string?>(null) : _actual.GetPrincipalAsync(token);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "files-native-setup-" + Guid.NewGuid().ToString("N"));
        private readonly List<IBrowseNativeAppContentSession> _sessions = [];
        private readonly HashSet<IBrowseNativeAppContentSession> _failed = [];
        internal readonly PrincipalSource Principal = new();
        internal readonly CancellationTokenSource App = new(), Window = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly ServiceProvider Provider;
        internal readonly NativeFilesWorkspaceService Files;
        internal string Empty => Path.Combine(_root, "empty");
        internal string Nonempty => Path.Combine(_root, "existing");
        internal string PreservedFile => Path.Combine(Nonempty, "preserve.txt");
        internal string Profile = "";

        internal Rig()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture verifies actual Linux OS-local Home.");
            Directory.CreateDirectory(_root); File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(Empty); Directory.CreateDirectory(Nonempty); File.WriteAllText(PreservedFile, "Existing user data");
            IAppPaths paths = new Paths(_root);
            NativeFilesWorkspaceService? files = null; NativeFilesWorkspaceAuthority? authority = null; FilesArtifactResourceResolver? resolver = null;
            Home = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")), Principal, paths,
                new("9to1.files.setup.test." + Guid.NewGuid().ToString("N")),
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                }, configureOriginalResolvers: owner =>
                {
                    authority = new(files!, owner.Identity.Profiles, owner.Ownership); resolver = new(authority);
                    return new([resolver], new Dictionary<Type, object>
                    { [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver });
                });
            Files = files!;
            var services = new ServiceCollection(); services.AddSingleton(paths);
            services.AddHavenOwnedWindowsHomeDomain(Home, paths, Principal);
            services.AddSingleton(Files); services.AddSingleton(authority!); services.AddSingleton(resolver!);
            services.AddSingleton<HostLocalTaskActorSource>(); services.AddFilesNativeHost();
            Provider = services.BuildServiceProvider();
        }
        internal async Task StartAsync()
        { await Home.StartOriginalAsync().WaitAsync(Limit); Profile = (await Home.Profiles.GetCurrentAsync(TestContext.Current.CancellationToken))!.ProfileId; }
        internal BrowseAppContentRequest Request() => new(Guid.NewGuid(), AppContentAddress.Parse("9to1://files"), Profile, BrowserTabPrivacy.Standard);
        internal FilesBrowseAppContentSession Create(Func<Control, Task<string?>> picker, BrowseAppContentRequest? request = null)
        {
            var actual = request ?? Request();
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Window.Token);
            var route = NativeFilesDesktopRoute.BindOriginalSameProcess(Provider, Home, App.Token, lifetime.Token);
            var session = new FilesBrowseAppContentSession(actual, FilesBrowseAppContentAddress.Parse(actual.Address), route, lifetime, picker);
            _sessions.Add(session); return session;
        }
        internal IBrowseNativeAppContentSession Track(IBrowseAppContentSession session)
        { var native = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(session); _sessions.Add(native); return native; }
        internal void ExpectFailedClose(IBrowseNativeAppContentSession session) => _failed.Add(session);
        public async ValueTask DisposeAsync()
        {
            Principal.Refuse = false; List<Exception> errors = [];
            foreach (var session in _sessions)
            {
                var failure = await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Limit));
                if (failure is not null && !_failed.Contains(session)) errors.Add(failure);
            }
            try { await Home.CloseAndDrainAsync().WaitAsync(Limit); } catch (Exception error) { errors.Add(error); }
            try { await Provider.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            App.Dispose(); Window.Dispose();
            if (errors.Count > 0) throw new AggregateException("Files setup fixture retained at " + _root, errors);
            Directory.Delete(_root, true);
        }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}

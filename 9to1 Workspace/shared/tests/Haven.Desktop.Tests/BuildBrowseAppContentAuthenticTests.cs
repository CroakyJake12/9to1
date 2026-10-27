using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>
/// Authentic Build (Dev) native session controls over the SAME genuine Linux Home/Files/SQLite rig.
/// They drive the REAL <see cref="BuildBrowseAppContentSession"/> with real Dev services and a real
/// saved-project catalogue. Only the borrowed host Home readiness/admission/profile seam (a host
/// seam) is supplied by a deterministic test double; the production seam's real admission is the
/// <see cref="NativeFilesDesktopRoute"/>. These do not claim a real Home readiness producer. The
/// real-factory/same-process route cases require <see cref="HomeNativeWindowsComposition"/>/
/// <see cref="NativeFilesDesktopRoute.BindOriginalSameProcess"/> (Windows control) and are NOT RUN
/// in this lane.
/// </summary>
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [Fact]
    public async Task Build_template_composition_discovers_registered_content_providers_without_substituting_their_identity()
    {
        var token = TestContext.Current.CancellationToken;
        var extension = new RegisteredTemplateContentProvider();
        using var services = new ServiceCollection()
            .AddSingleton<IDeveloperProjectTemplateContentProvider>(extension)
            .BuildServiceProvider();

        var providers = BuildBrowseAppContentFactory.ResolveTemplateProviders(services);
        Assert.Contains(providers, provider => ReferenceEquals(provider, extension));
        Assert.Contains(providers, provider => provider is DeveloperCuiProjectTemplateProvider);
        var catalog = new DeveloperProjectCatalog(providers);
        var option = Assert.Single((await catalog.SearchAsync(new(ProviderId: extension.ProviderId), token).ConfigureAwait(false)).Value!);
        Assert.Equal(extension.ProviderId, option.Template.ProviderId);
        Assert.Equal("fixture-component", option.Template.TemplateId);
        var materializer = new DeveloperProjectTemplateMaterializer(providers);
        var prepared = await materializer.PrepareAsync(option, "Fixture Component", token).ConfigureAwait(false);
        Assert.True(prepared.Succeeded);
        Assert.Equal(extension.ProviderId, prepared.Value!.ProviderId);
        Assert.Equal("Widget.cui", Assert.Single(prepared.Value.Files).RelativePath);
    }

    private sealed class RegisteredTemplateContentProvider : IDeveloperProjectTemplateContentProvider
    {
        internal const string ActualProviderId = "test.registered-template-provider";
        private static readonly DeveloperProjectTemplate Template = new("fixture-component", "Fixture CUI component", "component",
            ["CUI"], [], [], ["Build"], [], ActualProviderId, false, ["CUI runtime"]);
        public string ProviderId => ActualProviderId;
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([Template]);
        }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeveloperToolchainReadiness>>([]);
        }
        public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(
            DeveloperProjectTemplate template, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(template.ProviderId, ProviderId) ||
                !StringComparer.Ordinal.Equals(template.TemplateId, Template.TemplateId))
                throw new InvalidOperationException("Only this provider's exact template is supported.");
            return Task.FromResult<IReadOnlyList<DeveloperProjectTemplateFile>>([new("Widget.cui", "<Cui version=\"1\" />")]);
        }
    }

    private sealed class BuildTestBorrowedResources : IBuildBorrowedResources
    {
        private readonly ICuiSceneReadiness _readiness;
        private readonly TaskCompletionSource _admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldAdmission;
        internal bool Closed;
        internal TaskCompletionSource? AdmissionEntered;
        internal Task AdmissionTask => _admission.Task;
        // The genuine authenticated profile this seam represents. Tests may change it to simulate
        // revocation/actor replacement so revalidation (not a cached label) is observed.
        internal string Profile { get; set; }

        internal BuildTestBorrowedResources(string profile, ICuiSceneReadiness? readiness = null, bool holdAdmission = false)
        {
            Profile = profile;
            _readiness = readiness ?? ReadyReadiness.Instance;
            HoldAdmission = holdAdmission;
        }

        public ICuiSceneReadiness BorrowOriginalHomeReadiness() => _readiness;
        public void DemandOriginalHomeJoin() { }
        public Task CloseAndDrainAsync() { Closed = true; return Task.CompletedTask; }

        public Task AdmitOriginalShellInitializationAsync(CancellationToken cancellationToken)
        {
            // Signal that the genuine admission barrier was actually entered before close can run.
            AdmissionEntered?.TrySetResult();
            // A held admission is settled by close/abort cancellation; otherwise it completes so the
            // session proceeds to the genuine profile revalidation below.
            _ = cancellationToken.Register(() => _admission.TrySetCanceled(cancellationToken));
            if (!HoldAdmission) _admission.TrySetResult();
            return _admission.Task;
        }

        public void CheckOriginalAppContentProfile(string expectedProfile)
        {
            if (!string.Equals(expectedProfile, Profile, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This Build tab belongs to a different authenticated Home profile.");
        }

        public Task RevalidateOriginalAppContentAsync(string expectedProfile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(expectedProfile, Profile, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This Build tab belongs to a different authenticated Home profile.");
            return Task.CompletedTask;
        }

        public Task? OriginalAdmission => _admission.Task;
    }

    private sealed class ReadyReadiness : ICuiSceneReadiness
    {
        public static readonly ReadyReadiness Instance = new();
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "FixtureHomeReady", "The authentic fixture Home boundary is ready."));
    }

    private sealed record BuildLandingState(SpaceRegistry Spaces, SpaceDevelopmentReferenceCatalog Catalog, BuildTestBorrowedResources Borrowed);

    private static BuildLandingState BuildLanding(Rig rig, bool holdAdmission = false)
    {
        var spaces = rig.Provider.GetRequiredService<SpaceRegistry>();
        var conversations = rig.Provider.GetRequiredService<IConversationRepository>();
        var canonical = rig.Provider.GetRequiredService<TaskExecutionCoordinator>();
        var development = rig.Provider.GetRequiredService<DeveloperTaskWorkspaceService>();
        var actors = rig.Provider.GetRequiredService<IAuthenticatedResourceActorSource>();
        var taskViews = new SpaceTaskWorkspaceService(spaces, conversations, canonical);
        var workspace = new SpaceDevelopmentWorkspace(spaces, taskViews, development);
        var catalog = new SpaceDevelopmentReferenceCatalog(spaces, taskViews, workspace, actors);
        var borrowed = new BuildTestBorrowedResources(rig.Actor.ProfileId, holdAdmission: holdAdmission);
        return new(spaces, catalog, borrowed);
    }

    private static BrowseAppContentRequest LandingRequest(Rig rig) =>
        new(Guid.NewGuid(), AppContentAddress.Parse("9to1://dev"), rig.Actor.ProfileId, BrowserTabPrivacy.Standard);

    private static BuildBrowseAppContentSession CreateSession(Rig rig, BuildLandingState landing, BrowseAppContentRequest request,
        CancellationTokenSource lifetime, Func<BrowseNavigationIntent, Task, bool>? queueNavigation = null)
    {
        var workspaces = rig.Provider.GetRequiredService<IDeveloperWorkspaceStore>();
        var factory = rig.Provider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>();
        var session = new BuildBrowseAppContentSession(request, BuildBrowseAppContentAddress.Parse(request.Address),
            workspaces, rig.Provider.GetRequiredService<DeveloperTaskWorkspaceService>(),
            rig.Provider.GetRequiredService<TaskExecutionCoordinator>(), factory,
            landing.Spaces, landing.Catalog, lifetime, queueNavigation);
        session.AttachBorrowed(landing.Borrowed);
        return session;
    }

    /// <summary>The normal Build landing presents the real saved-project picker (not a placeholder or
    /// NotSupported), publishes, reports a clean close, and retires cleanly. The borrowed home seam
    /// is the host seam only; the session's genuine admission/profile revalidation is exercised.</summary>
    [AvaloniaFact]
    public async Task Authentic_Build_landing_presents_the_real_saved_project_picker_and_closes_cleanly()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            using var lifetime = new CancellationTokenSource();
            var session = CreateSession(rig, landing, LandingRequest(rig), lifetime);

            await session.InitializeAsync(token);
            Assert.True(session.IsPublicationCurrent);
            var view = Assert.IsType<Border>(session.OriginalNativeView);
            Assert.IsType<DeveloperSavedProjectsPage>(view.Child);

            Assert.True(await session.TryPrepareToCloseAsync(token));
            await session.CloseAndDrainAsync();
            Assert.True(session.CanRetireOriginalView);
            Assert.True(landing.Borrowed.Closed);
        });
    }

    /// <summary>A request whose profile label disagrees with the ACTUAL admitted actor cannot publish:
    /// a non-blank profile label is not authorization. Close joins the failed original initialization
    /// and rethrows its retained primary fault exactly once (stable close identity), while borrowed
    /// owner cleanup still runs.</summary>
    [AvaloniaFact]
    public async Task Authentic_Build_landing_refuses_foreign_request_profile_and_close_rethrows_original_fault()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            // Force the admitted profile to disagree with the genuine actor's profile.
            landing = landing with { Borrowed = new BuildTestBorrowedResources("foreign-profile") };
            using var lifetime = new CancellationTokenSource();
            var session = CreateSession(rig, landing, LandingRequest(rig), lifetime);

            var failure = await Record.ExceptionAsync(() => session.InitializeAsync(token));
            Assert.NotNull(failure);
            Assert.Contains(Flatten(failure!), error => error is UnauthorizedAccessException);
            Assert.False(session.IsPublicationCurrent);

            // Close joins the failed original initialization and rethrows its retained primary fault.
            var close = session.CloseAndDrainAsync();
            var closeAgain = session.CloseAndDrainAsync();
            Assert.Same(close, closeAgain); // stable close task identity.
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure);
            Assert.Contains(Flatten(closeFailure!), error => error is UnauthorizedAccessException);
            Assert.False(session.CanRetireOriginalView); // close faulted, not a clean retirement.
            Assert.True(landing.Borrowed.Closed); // borrowed owner cleanup still ran.
        });
    }

    /// <summary>Close during a held initialization settles the genuine admission ORIGINAL after the
    /// admission barrier is actually entered. Canceled admission/initialization are retained as
    /// failures and close rethrows them; borrowed cleanup still runs.</summary>
    [AvaloniaFact]
    public async Task Authentic_Build_close_during_held_initialization_rethrows_canceled_admission()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig, holdAdmission: true);
            landing.Borrowed.AdmissionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var lifetime = new CancellationTokenSource();
            var session = CreateSession(rig, landing, LandingRequest(rig), lifetime);

            var initialization = session.InitializeAsync(token);
            Assert.False(initialization.IsCompleted); // held at genuine admission.

            // Wait until the genuine admission barrier is actually entered before closing.
            await landing.Borrowed.AdmissionEntered.Task.WaitAsync(token);
            var close = session.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure); // canceled admission/initialization retained as failures.
            Assert.False(session.CanRetireOriginalView);
            Assert.True(landing.Borrowed.Closed);

            var settled = await Record.ExceptionAsync(() => initialization);
            Assert.NotNull(settled); // admission was canceled by close, not fabricated as success.
        });
    }

    /// <summary>Revalidation is the genuine authority: a revoked/foreign current actor is refused at
    /// publication boundary, not merely a cached label mismatch. The session never publishes.</summary>
    [AvaloniaFact]
    public async Task Authentic_Build_revalidation_refuses_revoked_foreign_actor_at_publication()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            using var lifetime = new CancellationTokenSource();
            var session = CreateSession(rig, landing, LandingRequest(rig), lifetime);

            // Revoke: change the borrowed seam to a different actor profile so the genuine revalidation
            // (the same contract the production route serves) refuses the request label. This mutates the
            // retained seam, not AttachBorrowed (which rejects replacement of an already-attached owner).
            landing.Borrowed.Profile = "revoked-actor";

            var failure = await Record.ExceptionAsync(() => session.InitializeAsync(token));
            Assert.NotNull(failure);
            Assert.Contains(Flatten(failure!), error => error is UnauthorizedAccessException);
            Assert.False(session.IsPublicationCurrent);
        });
    }

    private static IEnumerable<Exception> Flatten(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var value in Flatten(child)) yield return value;
    }
}

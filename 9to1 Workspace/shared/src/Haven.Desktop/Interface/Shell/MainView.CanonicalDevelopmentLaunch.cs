using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using Haven.Infrastructure;
using HavenOS.Apps.Spaces.Development;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private OriginalDevelopmentCatalogBinding? _originalDevelopmentCatalog;

    // Source-owned native window composition only. All values are the SAME retained graph,
    // not serialized app/project tuples. This registers no actor, grant, provider or business owner.
    internal void ConfigureOriginalDevelopmentCatalog(NativeFilesDesktopRoute originalFiles,
        TaskRunPermissionAuthority originalAuthority, HostLocalTaskActorSource originalTaskActors,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(originalFiles); ArgumentNullException.ThrowIfNull(originalAuthority);
        ArgumentNullException.ThrowIfNull(originalTaskActors);
        if (_originalDevelopmentCatalog is not null)
            throw new InvalidOperationException("The original development catalogue is already bound.");
        var routes = _canonicalSpaceRoutes ?? throw new InvalidOperationException("The original canonical Space graph is unconfigured.");
        var development = routes.Development ?? throw new InvalidOperationException("The SAME original Dev owner is unconfigured.");
        if (!ReferenceEquals(_originalFilesRoute, originalFiles) || _captureOriginalCanonicalDevPage is null ||
            !routes.Canonical.HasOriginalAdmissionAuthority(originalAuthority) || !originalAuthority.HasOriginalTaskActorSource(originalTaskActors))
            throw new UnauthorizedAccessException("Retain the SAME native Files, canonical Task authority and configured Task actor source.");
        if (!originalConnectionLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Home connection and owning native window lifetimes.");
        var readiness = originalFiles.BorrowOriginalHomeReadinessForMetadata(); // SAME source, not a successful-task adapter.
        var catalog = new SpaceDevelopmentReferenceCatalog(SpacesRegistry, routes.Source, development, originalTaskActors);
        _originalDevelopmentCatalog = new(originalFiles, originalAuthority, originalTaskActors, catalog,
            readiness, originalConnectionLifetime, originalWindowLifetime, routes);
    }

    // Initial executable route only; the caller already owns genuine App startup and
    // the SAME native shell/window. Return the existing original shell Task unchanged.
    internal Task OpenOriginalInitialDevelopmentCatalogAsync(CancellationToken originalAppToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        _originalShellWork.DemandAdmission();
        originalAppToken.ThrowIfCancellationRequested();
        return OpenOriginalDevelopmentCatalogAsync(false);
    }

    private Task OpenOriginalDevelopmentCatalogAsync(bool forceNewTab) =>
        _originalShellWork.RunAsync(original => OpenOriginalDevelopmentCatalogAsync(original, forceNewTab));

    private async Task OpenOriginalDevelopmentCatalogAsync(DesktopOriginalWorkLifetime.Original original, bool forceNewTab)
    {
        original.BindPublicationGuard(() => !IsDisposed);
        original.DemandPublication();
        var binding = _originalDevelopmentCatalog;
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            // This is a presentation tab key only. No conversation, Task, Run or project is created.
            var key = forceNewTab ? "dev-saved-projects-" + Guid.NewGuid().ToString("N") : "dev-saved-projects";
            var existing = OpenTabs.FirstOrDefault(tab => tab.Key == key);
            if (existing is not null) { SelectedTab = existing; return; }
            DemandCanonicalPageAcquisition();
            DeveloperSavedProjectsPage? retained = null;
            void CapturePartial(DeveloperSavedProjectsPage actual)
            {
                RetainOriginalCanonicalPage(actual); // Before constructor host/document/native publication.
                if (retained is not null && !ReferenceEquals(retained, actual))
                    throw new InvalidOperationException("The native catalogue factory acquired multiple different partial pages.");
                retained = actual;
            }
            DeveloperSavedProjectsPage page;
            if (binding is null)
                page = AcquireOriginalShellSynchronous(original, () => DeveloperSavedProjectsPage.CreateSetupRequired(CapturePartial));
            else
            {
                DemandOriginalDevelopmentCatalog(binding);
                page = AcquireOriginalShellSynchronous(original, () => new DeveloperSavedProjectsPage(binding.Catalog, SpacesRegistry,
                    binding.HomeReadiness, binding.Files.DemandExternalOriginalRetirementJoin,
                    view => OpenOriginalDevelopmentCatalogTaskAsync(binding, view),
                    spaceId => OpenOriginalCanonicalTaskSpaceAsync(spaceId),
                    binding.ConnectionLifetime, binding.WindowLifetime, CapturePartial));
                DemandOriginalDevelopmentCatalog(binding);
            }
            if (!ReferenceEquals(retained, page))
            {
                RetainOriginalCanonicalPage(page);
                throw new InvalidOperationException("The SAME actual catalogue page was not retained before publication.");
            }
            original.DemandPublication();
            AddOrSelectTab(key, "Dev", page, true, HavenSurface.Studio, forceNewTab: true);
            // ApplySelectedTab owns the one actual ActivateAsync. Do not activate it twice.
        });
    }

    private Task OpenOriginalDevelopmentCatalogTaskAsync(OriginalDevelopmentCatalogBinding binding, SpaceDeveloperView captured) =>
        _originalShellWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !IsDisposed);
            original.DemandPublication();
            void OwnRawSource(Action callback) => AcquireOriginalShellSynchronous(original, () => { callback(); return true; });
            // Recheck the exact same saved selection again under this shell's original custody.
            var snapshot = captured.Task.Snapshot ?? throw new InvalidOperationException("The existing saved task snapshot is required.");
            var row = new SpaceDevelopmentReferenceRow(captured.Space.Id, captured.Space.Revision, captured.ContextReferenceId,
                captured.Link, captured.Space.Name, captured.Project.Project.Name,
                snapshot.PersistenceRevision, snapshot.CheckpointId);
            var current = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
                () => binding.Catalog.OpenAsync(row, original.Token, OwnRawSource)));
            await PublishOriginalCanonicalTabAsync(original, () => DemandOriginalDevelopmentCatalog(binding));
            // Existing Task opening revalidates SAME Task/Run/context and acquires its real native
            // frame. Its existing Dev action supplies the active original Task readiness issuer.
            // No ConfigureTaskMode, Submit, Begin, Resume or accepted-effect replay occurs here.
            await OpenOriginalCanonicalTaskAsync(original, current.Task);
        });

    private void DemandOriginalDevelopmentCatalog(OriginalDevelopmentCatalogBinding binding)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!ReferenceEquals(_originalDevelopmentCatalog, binding) || !ReferenceEquals(_canonicalSpaceRoutes, binding.Routes) ||
            !ReferenceEquals(_originalFilesRoute, binding.Files) ||
            !binding.Routes.Canonical.HasOriginalAdmissionAuthority(binding.Authority) ||
            !binding.Authority.HasOriginalTaskActorSource(binding.Actors) ||
            !binding.Catalog.IsBoundToOriginalComposition(SpacesRegistry, binding.Routes.Source, binding.Routes.Development!, binding.Actors) ||
            !ReferenceEquals(binding.Files.BorrowOriginalHomeReadinessForMetadata(), binding.HomeReadiness))
            throw new UnauthorizedAccessException("The original native development catalogue composition changed.");
        binding.ConnectionLifetime.ThrowIfCancellationRequested(); binding.WindowLifetime.ThrowIfCancellationRequested();
    }

    private sealed record OriginalDevelopmentCatalogBinding(NativeFilesDesktopRoute Files,
        TaskRunPermissionAuthority Authority, HostLocalTaskActorSource Actors, SpaceDevelopmentReferenceCatalog Catalog,
        ICuiSceneReadiness HomeReadiness, CancellationToken ConnectionLifetime, CancellationToken WindowLifetime,
        CanonicalSpaceRoutes Routes);
}

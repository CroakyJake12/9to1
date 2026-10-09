#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Pages.Write;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    // Candidate SAME-process route only. Installed-peer admission and concurrent
    // separate product executables require their protected Home host contract.
    internal Task OpenOriginalInitialDocumentProductAsync(string product, IServiceProvider provider,
        HomeNativeWindowsComposition home, CancellationToken appLifetime, CancellationToken windowLifetime) =>
        _originalShellWork.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            if (product is not ("write" or "present"))
                throw new InvalidDataException("The initial document product is unsupported.");
            if (!ReferenceEquals(provider, global::Haven.Desktop.App.Services))
                throw new UnauthorizedAccessException("Retain the original document provider.");
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
            original.BindPublicationGuard(() => !IsDisposed && ReferenceEquals(provider, global::Haven.Desktop.App.Services));
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(original.Token, appLifetime, windowLifetime);
            var token = lifetime.Token;
            void OwnSource(Action callback) => AcquireOriginalShellSynchronous(original, () => { callback(); return true; });
            void DemandPublication() { token.ThrowIfCancellationRequested(); original.DemandPublication(); }
            var observed = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () =>
                WindowsHomeSameProcessRuntimeObservation.CheckAsync(provider, home, appLifetime, windowLifetime,
                    OwnSource, DemandPublication, token)));
            if (observed.State != CuiSceneAvailabilityState.Ready)
                throw new InvalidOperationException(observed.Message);

            Control? page = null;
            Exception? primary = null;
            try
            {
                DemandPublication();
                DemandCanonicalPageAcquisition();
                void CapturePartial(Control actual)
                {
                    if (page is not null && !ReferenceEquals(page, actual))
                        throw new InvalidOperationException("The original document factory supplied a different page.");
                    page = actual;
                    RetainOriginalCanonicalPage(actual); // Before native constructor callbacks, including failure.
                    DemandPublication();
                }
                var created = AcquireOriginalShellSynchronous(original, () => CreateDocumentWorkspace(product, CapturePartial));
                if (!ReferenceEquals(page, created))
                    throw new InvalidOperationException("The document factory did not retain its actual original page.");
                if (created is WritePage write)
                    await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => write.InitializeAsync(token)));
                else if (created is PresentPage present)
                    await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => present.InitializeAsync(token)));
                else throw new InvalidOperationException("The initial document product did not create its actual page.");
                if (created is WritePage initializedWrite) initializedWrite.DemandOriginalInitializedDocument();
                else ((PresentPage)created).DemandOriginalInitializedDocument();

                await PublishOriginalCanonicalTabAsync(original, () =>
                {
                    DemandPublication();
                    var current = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(provider, home, observed);
                    if (current.State != CuiSceneAvailabilityState.Ready)
                        throw new InvalidOperationException(current.Message);
                    void Publish()
                    {
                        DemandPublication();
                        if (created is WritePage readyWrite) readyWrite.DemandOriginalInitializedDocument();
                        else ((PresentPage)created).DemandOriginalInitializedDocument();
                        var key = "app-" + product;
                        var existing = OpenTabs.Where(tab => tab.Key == key).ToArray();
                        if (existing.Length > 1 || existing.Any(tab => !ReferenceEquals(tab.Page, created)))
                            throw new UnauthorizedAccessException("Retain one initial original document tab.");
                        var surface = product == "write" ? HavenSurface.Write : HavenSurface.Present;
                        AddOrSelectTab(key, product == "write" ? "Write" : "Present", created, false, surface, false);
                        DemandPublication();
                        if (SelectedTab is not { } selected || selected.Key != key ||
                            !ReferenceEquals(selected.Page, created) || CurrentSurface != surface)
                            throw new UnauthorizedAccessException("The original document tab changed during publication.");
                    }
                    if (created is WritePage writePage) writePage.PublishOriginalDocumentTab(Publish);
                    else ((PresentPage)created).PublishOriginalDocumentTab(Publish);
                });
                return;
            }
            catch (Exception error) { primary = error; original.Retain(error); }
            // A failed constructor or mount still owns the captured actual partial.
            // Borrowed Home/provider remain alive under their existing App host.
            if (page is IDesktopOriginalRetirementParticipant participant)
            {
                Task? close = null;
                try { close = AcquireOriginalShellSynchronous(original, participant.CloseAndDrainAsync); }
                catch (Exception error) { original.Retain(error); }
                if (close is not null)
                    try { await original.AwaitAsync(close); }
                    catch (Exception error) { original.Retain(close.IsFaulted ? close.Exception! : error); }
            }
            else if (page is not null) original.Retain(new DesktopOriginalRetirementUnavailableException(page.GetType()));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary!).Throw();
        });
}
#endif

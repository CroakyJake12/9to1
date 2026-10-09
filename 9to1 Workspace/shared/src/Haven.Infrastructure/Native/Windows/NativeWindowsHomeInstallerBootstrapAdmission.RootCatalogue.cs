using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstallerBootstrapAdmission
{
    private readonly ConditionalWeakTable<OriginalRootPublisherCatalogue, Invocation> _originalRootCatalogues = new();
    private Task<OriginalInstaller?>? _originalRootCatalogueReader;
    private System.IO.Pipes.NamedPipeClientStream? _originalRootCataloguePipe;
    private string? _originalRootProtectedCatalogueDigest;
    private void PruneOriginalHealthyFiniteSources() => _originals.RemoveAll(source =>
        source.Driver.IsCompletedSuccessfully && source.Source.OriginalErrors.Count == 0 &&
        source.Source.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
        (source.Process is null || source.ProcessClose?.IsCompletedSuccessfully == true) &&
        source.Resources.All(held => held.Close?.IsCompletedSuccessfully == true));

    /// <summary>The SAME current-process signature/catalogue reader is reused for
    /// Root. This observation authenticates publisher/catalogue bytes only and is
    /// never accepted by the installer choice or initial publisher enrollment APIs.
    /// Protected enrolled package/activation binding remains a separate admission.</summary>
    public sealed class OriginalRootPublisherCatalogue
    {
        internal readonly NativeWindowsHomeInstallerBootstrapAdmission Issuer;
        internal readonly OriginalInstaller SignedProcess;
        internal OriginalRootPublisherCatalogue(NativeWindowsHomeInstallerBootstrapAdmission issuer, OriginalInstaller actual)
        { Issuer = issuer; SignedProcess = actual; }
        public string CatalogueRevision => SignedProcess.CatalogueRevision;
        public string CatalogueSha256 => SignedProcess.CatalogueSha256;
        public string PublisherCertificateSha256 => SignedProcess.PublisherCertificateSha256;
        public string RootPackageId => SignedProcess._catalogue.RootPackageId;
        public string HomePackageId => SignedProcess._catalogue.HomePackageId;
    }

    public Task<OriginalRootPublisherCatalogue?> InspectOriginalRootPublisherCatalogueWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        InspectRootCatalogue(null, scope, retain, token);

    public Task<OriginalRootPublisherCatalogue?> InspectOriginalConnectedRootPublisherCatalogueWithinSourceAsync(
        System.IO.Pipes.NamedPipeClientStream actualConnectedRootPipe,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        InspectRootCatalogue(actualConnectedRootPipe ?? throw new ArgumentNullException(nameof(actualConnectedRootPipe)), scope, retain, token);

    internal Task<OriginalRootPublisherCatalogue?> InspectOriginalProtectedRootPublisherCatalogueWithinSourceAsync(
        NativeWindowsHomeInstalledRootAdmission.OriginalProtectedEnrollmentCatalogue sameProtectedCatalogue,
        System.IO.Pipes.NamedPipeClientStream? actualConnectedRootPipe,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        InspectRootCatalogue(actualConnectedRootPipe, scope, retain, token, sameProtectedCatalogue);

    private Task<OriginalRootPublisherCatalogue?> InspectRootCatalogue(System.IO.Pipes.NamedPipeClientStream? actualPipe,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        NativeWindowsHomeInstalledRootAdmission.OriginalProtectedEnrollmentCatalogue? actualProtectedCatalogue = null)
    {
        var work = Admit(scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OriginalRootPublisherCatalogue?> actual;
        lock (_gate)
        {
            DemandAdmission(); actual = DriveRoot(start.Task); work.Driver = actual; _originals.Add(work);
        }
        Publish(work, retain, start); return actual;

        async Task<OriginalRootPublisherCatalogue?> DriveRoot(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            OriginalRootPublisherCatalogue? result = null;
            try
            {
                string? protectedDigest = null;
                if (actualProtectedCatalogue is not null) work.Source.Invoke(() =>
                {
                    actualProtectedCatalogue.Owner.DemandOriginalProtectedCatalogueCurrent(actualProtectedCatalogue, this);
                    protectedDigest = actualProtectedCatalogue.OriginalEnrollmentDigest; return true;
                });
                Invocation? child = null; TaskCompletionSource? begin = null;
                HomeNativeObservedPeer? actualPeer = null;
                if (actualPipe is not null) work.Source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual connected Windows Root required.");
                    actualPeer = ReadOriginalRootServer(actualPipe, work); return true;
                });
                Task<OriginalInstaller?> reader;
                lock (_gate)
                {
                    DemandAdmission();
                    if (_originalRootCatalogueReader is null)
                    {
                        child = Admit(body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw));
                        begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        reader = Inspect(child, begin.Task, raw => Keep(work, retain, raw), token, originalInstallerEntry: false,
                            actualConnectedRootPeer: actualPeer, actualProtectedCatalogue: actualProtectedCatalogue);
                        child.Driver = reader; _originals.Add(child); _originalRootCatalogueReader = reader; _originalRootCataloguePipe = actualPipe;
                        _originalRootProtectedCatalogueDigest = protectedDigest;
                    }
                    else
                    {
                        if (!ReferenceEquals(_originalRootCataloguePipe, actualPipe) || _originalRootProtectedCatalogueDigest != protectedDigest)
                            throw new UnauthorizedAccessException("The cached actual Root signature source belongs to another process/connection.");
                        reader = _originalRootCatalogueReader;
                    }
                }
                if (child is not null) Publish(child, raw => Keep(work, retain, raw), begin!);
                var observed = await Read(work, retain, () => reader).ConfigureAwait(false);
                if (observed is not null)
                {
                    work.Source.Invoke(() =>
                    {
                        if (observed.Original.ProcessClose is not null ||
                            !reader.IsCompletedSuccessfully || !ReferenceEquals(reader.Result, observed) ||
                            observed.Original.Source.OriginalErrors.Count != 0 || observed.Original.Source.OriginalTasks.Any(raw => !raw.IsCompletedSuccessfully))
                            throw new UnauthorizedAccessException("The actual signed Root catalogue reader did not settle independently.");
                        if (actualPipe is not null)
                        {
                            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual connected Windows Root required.");
                            if (ReadOriginalRootServer(actualPipe, work) != actualPeer || observed.Original.Process!.ProcessId != actualPeer!.ProcessId ||
                                observed.Original.Process.OperatingSystemPrincipalId != actualPeer.OperatingSystemPrincipalId)
                                throw new UnauthorizedAccessException("The actual Root server PID/principal changed during publisher authentication.");
                        }
                        if (actualProtectedCatalogue is not null)
                        {
                            actualProtectedCatalogue.Owner.DemandOriginalProtectedCatalogueCurrent(actualProtectedCatalogue, this);
                            if (observed.CatalogueSha256 != actualProtectedCatalogue.OriginalCatalogueSha256 ||
                                observed.PublisherCertificateSha256 != actualProtectedCatalogue.OriginalPublisherCertificateSha256)
                                throw new UnauthorizedAccessException("The actual Root signer/catalogue differs from the exact protected installer transfer.");
                        }
                        result = new(this, observed); return true;
                    });
                }
                else work.UnavailableReason = child?.UnavailableReason ?? "AuthenticatedSignedRootCatalogueRequired";
            }
            catch (Exception cause) { work.Source.Retain(cause); }
            await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (work.Source.OriginalErrors.Count != 0)
                throw new AggregateException("Actual Root publisher/catalogue source failed; retained evidence cannot qualify as absent.", work.Source.OriginalErrors);
            if (result is not null) _originalRootCatalogues.Add(result, work);
            return result;
        }
    }

    public bool IsIssuedOriginalRootPublisherCatalogue(OriginalRootPublisherCatalogue same) => same is not null &&
        ReferenceEquals(same.Issuer, this) && _originalRootCatalogues.TryGetValue(same, out var source) &&
        source.Driver.IsCompletedSuccessfully && source.Driver is Task<OriginalRootPublisherCatalogue?> task &&
        ReferenceEquals(task.Result, same) && source.Source.OriginalErrors.Count == 0 &&
        source.Source.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
        same.SignedProcess.Original.ProcessClose is null &&
        !IsIssuedOriginalInstaller(same.SignedProcess);

    internal Task DemandOriginalRootPublisherCurrentWithinSourceAsync(OriginalRootPublisherCatalogue same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = Admit(scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task actual;
        lock (_gate) { DemandAdmission(); actual = Demand(start.Task); work.Driver = actual; _originals.Add(work); }
        Publish(work, retain, start); return actual;

        async Task Demand(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                if (!OperatingSystem.IsWindows() || !IsIssuedOriginalRootPublisherCatalogue(same))
                    throw new UnauthorizedAccessException("The SAME actual signed Root process/catalogue is required.");
                var actor = await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false);
                work.Source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows Root required.");
                    if (actor != same.SignedProcess.OriginalActor)
                        throw new UnauthorizedAccessException("The original Root OS profile changed.");
                    VerifyWindowsSignature(work, same.SignedProcess.Original.Process!.ExecutablePath);
                    if (!IsExplicitlyEnrolledPublisher(work, same.SignedProcess.Signer))
                        throw new UnauthorizedAccessException("The actual explicitly enrolled Root publisher changed.");
                    lock (same.SignedProcess.NativeGate) same.SignedProcess.Original.Process!.DemandCurrent();
                    return true;
                });
                if (actor != await Read(work, retain, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => work.Source.Invoke(() => { body(); return true; }), raw => Keep(work, retain, raw), token)).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The actual Root actor changed during publisher validation.");
            }
            catch (Exception cause) { work.Source.Retain(cause); }
            finally { await CloseResources(work).ConfigureAwait(false); }
            await work.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (work.Source.OriginalErrors.Count != 0)
                throw new AggregateException("Original Root publisher/currentness failed; no runtime admission follows.", work.Source.OriginalErrors);
        }
    }
}

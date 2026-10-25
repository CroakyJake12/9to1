using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Explicit initial Linux installer activation using the SAME already-enrolled
/// publisher and canonical package journal. No installed peer, controlled launch, service
/// registration or Ready follows. Never used by ordinary Home/app startup.</summary>
public sealed class LinuxHomeBootstrapActivationOwner(LinuxHomePublisherEnrollmentOwner sameEnrollmentOwner)
{
    public sealed record Request(LinuxHomePublisherEnrollmentOwner.Request SigningInputs,
        string ExistingActivationParent, string StageLeaf, string PlanPath);
    public sealed record Plan(int SchemaVersion, IReadOnlyList<PackageLayout> Packages);
    public sealed record PackageLayout(string PackageId, string RelativeRoot, string SignedReceiptRelativePath, string CompletePayloadRelativePath);
    public sealed record Receipt(int SchemaVersion, string PackageId, string AppId, string Version,
        string Platform, string Abi, string EntrypointRelativePath, IReadOnlyList<HomePackageActivatedFile> Files);
    public sealed record Result(Guid OperationId, string ActivePath, long RegistryRevision, IReadOnlyList<string> PackageIds)
    { public bool AlreadySettled { get; init; } }
    private sealed record JournalBinding(int SchemaVersion, string ActivationParent, string StageLeaf,
        string PlanSha256, PackageLayout Layout, string SignedReceiptSha256, string PayloadSha256, string CatalogueRevision);
    public sealed class ActivationUnconfirmedException : IOException
    {
        public Guid OperationId { get; }
        public string? ObservedActivePath { get; }
        internal ActivationUnconfirmedException(Guid operation, string? active, Exception cause)
            : base("Initial activation has a retained canonical journal; its final outcome requires owning recovery. Do not redispatch or delete its stage/active bundle.", cause)
        { OperationId = operation; ObservedActivePath = active; }
    }
    private sealed record Verified(PackageLayout Layout, LinuxHomePackageOriginalArtifactProvider.Row Row, Receipt Receipt);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    { MaxDepth = 24, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public Task<Result> ActivateAsync(Request request, CancellationToken token) => RunAsync(request, recovery: false, token);
    /// <summary>Inspect the exact original retained operation and already-published
    /// bundle, then settle metadata only. Never repeats mode transitions or renames.</summary>
    public Task<Result> RecoverAsync(Request request, CancellationToken token) => RunAsync(request, recovery: true, token);

    internal Task<Result> WithinSettledCodeAsync(Request request,
        Func<PublishedCode, CancellationToken, Task> originalRuntime, CancellationToken token)
        => RunAsync(request, recovery: true, token, originalRuntime ?? throw new ArgumentNullException(nameof(originalRuntime)));

    internal sealed class PublishedCode
    {
        private readonly LinuxHomePackageArtifactStore _artifacts;
        private readonly LinuxHomePackageArtifactStore.ValidatedStage _stage;
        private readonly Func<Task> _source;
        private bool _closed;
        internal IReadOnlyList<HomePackageOriginalInstalledActivationRecord> Packages { get; }
        internal PublishedCode(LinuxHomePackageArtifactStore artifacts, LinuxHomePackageArtifactStore.ValidatedStage stage,
            IReadOnlyList<HomePackageOriginalInstalledActivationRecord> packages, Func<Task> source)
        { _artifacts = artifacts; _stage = stage; Packages = packages; _source = source; }
        internal async Task DemandCurrentAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_closed, this); await _source().ConfigureAwait(false);
            using var current = _artifacts.ValidatePublishedMachineCode(_stage.Files, _stage.Entrypoint, token);
            if (!_stage.Identity.SameReadVersion(current.Identity) || _stage.FileIdentities.Any(pair =>
                !current.FileIdentities.TryGetValue(pair.Key, out var actual) || !pair.Value.SameReadVersion(actual)))
                throw new UnauthorizedAccessException("The original retained published code cohort changed during Root runtime.");
        }
        internal async Task DemandPinnedCurrentAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_closed, this); await _source().ConfigureAwait(false);
            LinuxOriginalDownloadPaths.DemandPath(_stage.Directory, _stage.StagePath, true);
            if (!_stage.Identity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(_stage.Directory)))
                throw new UnauthorizedAccessException("The retained published machine-code root changed.");
            foreach (var file in _stage.Files)
            {
                token.ThrowIfCancellationRequested();
                using var held = LinuxOriginalDownloadPaths.OpenAt(_stage.Directory, file.RelativePath, 0x80000UL | 0x800UL);
                LinuxOriginalDownloadPaths.DemandPath(held, Path.Combine(_stage.StagePath, file.RelativePath), false);
                if (!_stage.FileIdentities[file.RelativePath].SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(held)))
                    throw new UnauthorizedAccessException("The retained already-hashed publisher code cohort changed.");
            }
        }
        internal SafeFileHandle OpenOriginalFile(HomePackageOriginalInstalledActivationRecord package, string relative)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!Packages.Any(value => ReferenceEquals(value, package)) ||
                !package.ActivatedFiles.Any(value => value.RelativePath == relative))
                throw new UnauthorizedAccessException("The same original signed runtime-cohort file is required.");
            var path = Path.Combine(package.InstallationRoot, relative);
            var held = LinuxOriginalDownloadPaths.OpenAt(_stage.Directory, Path.GetRelativePath(Path.GetDirectoryName(package.InstallationRoot)!, path), 0x80000UL | 0x800UL);
            try { LinuxOriginalDownloadPaths.DemandPath(held, path, false); return held; }
            catch { held.Dispose(); throw; }
        }
        internal void Close() => _closed = true;
    }

    private async Task<Result> RunAsync(Request request, bool recovery, CancellationToken token,
        Func<PublishedCode, CancellationToken, Task>? originalRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(sameEnrollmentOwner);
        Result? result = null;
        bool journalAttempted = false;
        string? observedActive = null;
        try
        {
            await sameEnrollmentOwner.WithinExistingEnrollmentAsync(request.SigningInputs, async (original, cancellation) =>
            {
                var operation = request.SigningInputs.OperationId.ToString("D");
                if (!recovery && (original.Registry.Packages.Count != 0 || original.Registry.RecentOperations.Count != 0))
                    throw new UnauthorizedAccessException("This command only performs an initial bootstrap. Existing package/journal state requires its original owning recovery or lifecycle operation.");
                if (recovery)
                {
                    var retained = original.Registry.RecentOperations;
                    if (retained.Count != original.Enrollment.SelectedPackageIds.Count || retained.Any(value =>
                        value.OperationId != operation || value.Action != "Install" ||
                        value.IdempotencyKey != "linux.bootstrap:" + operation + ":" + value.PackageId ||
                        value.State is not (HomePackageJournalState.Validated or HomePackageJournalState.Succeeded)) ||
                        !retained.Select(value => value.PackageId).Order(StringComparer.Ordinal).SequenceEqual(original.Enrollment.SelectedPackageIds.Order(StringComparer.Ordinal)))
                        throw new UnauthorizedAccessException("Recovery requires the exact original retained bootstrap operation and selected package cohort.");
                    journalAttempted = true; // the existing operation remains unresolved on every refusal
                }
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ExistingActivationParent));
                var stateDirectory = Path.GetDirectoryName(original.StatePath)!;
                // Never expose state, public signing inputs, or an existing app/profile
                // tree by recursively changing an ancestor's modes.
                if (!Path.IsPathFullyQualified(request.ExistingActivationParent) ||
                    Contains(parent, stateDirectory) || Contains(stateDirectory, parent) ||
                    Contains(parent, Path.GetFullPath(request.SigningInputs.PublicCertificatePath)) ||
                    Contains(parent, Path.GetFullPath(request.SigningInputs.SignedCataloguePath)) ||
                    Contains(parent, Path.GetFullPath(request.PlanPath)) ||
                    !LinuxOriginalDownloadPaths.IsSafeLeaf(request.StageLeaf) || request.StageLeaf == "active")
                    throw new ArgumentException("Use a distinct explicit root-owned code parent/stage, disjoint from state and administrative inputs.");
                LinuxHomePublisherEnrollmentOwner.DemandProtectedMachineDirectory(parent);
                using var parentHandle = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(parent, mutable: true);
                var parentIdentity = LinuxOriginalDownloadPaths.ReadIdentity(parentHandle);
                if ((parentIdentity.Mode & 0x1ff) != 0x1ed)
                    throw new UnauthorizedAccessException("The explicit code publication parent must already be root-owned mode0755; it is never adopted or chmodded.");
                var observedLeaf = recovery ? "active" : request.StageLeaf;
                using var stageDirectory = LinuxOriginalDownloadPaths.OpenAt(parentHandle, observedLeaf,
                    LinuxOriginalDownloadPaths.DirectoryFlags(mutable: true));
                var stagePath = Path.Combine(parent, observedLeaf);
                LinuxOriginalDownloadPaths.DemandPath(stageDirectory, stagePath, directory: true);
                LinuxOriginalDownloadPaths.DemandOwner(stageDirectory, 0, privateDirectory: !recovery);
                var stageIdentity = LinuxOriginalDownloadPaths.ReadIdentity(stageDirectory);
                using var planParent = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(Path.GetDirectoryName(Path.GetFullPath(request.PlanPath))!, mutable: false);
                var planBytes = ReadBounded(planParent, Path.GetDirectoryName(Path.GetFullPath(request.PlanPath))!, Path.GetFileName(request.PlanPath), 128 * 1024);
                var plan = JsonSerializer.Deserialize<Plan>(planBytes, Web) ?? throw new InvalidDataException("The explicit bundle plan is absent.");
                var layouts = plan.Packages?.Take(129).ToArray();
                if (plan.SchemaVersion != 1 || layouts is not { Length: >= 2 and <= 128 } || layouts.Any(value => value is null) ||
                    !layouts.Select(value => value.PackageId).Order(StringComparer.Ordinal).SequenceEqual(original.Enrollment.SelectedPackageIds.Order(StringComparer.Ordinal)) ||
                    layouts.Any(value => !LinuxOriginalDownloadPaths.IsSafeLeaf(value.RelativeRoot) ||
                        !HomePackageOriginalInstalledActivationRecord.SafeRelative(value.SignedReceiptRelativePath) ||
                        !HomePackageOriginalInstalledActivationRecord.SafeRelative(value.CompletePayloadRelativePath) ||
                        value.SignedReceiptRelativePath == value.CompletePayloadRelativePath) ||
                    layouts.Select(value => value.RelativeRoot).Distinct(StringComparer.Ordinal).Count() != layouts.Length)
                    throw new InvalidDataException("The bundle plan must exactly cover the enrolled selection with distinct relative package roots.");
                var verified = new List<Verified>();
                var complete = new List<HomePackageActivatedFile>();
                foreach (var layout in layouts)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var row = original.Rows[layout.PackageId]; var descriptor = row.Descriptor;
                    var receiptRelative = layout.RelativeRoot + "/" + layout.SignedReceiptRelativePath;
                    var receiptBytes = ReadBounded(stageDirectory, stagePath, receiptRelative, 2 * 1024 * 1024);
                    if (!Digest(receiptBytes).Equals(descriptor.SignedInstallationReceiptSha256, StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("The signed receipt differs from the exact enrolled package descriptor.");
                    var receipt = JsonSerializer.Deserialize<Receipt>(original.VerifyReceipt(receiptBytes), Web)
                        ?? throw new InvalidDataException("The signed installation receipt is absent.");
                    var files = receipt.Files?.Take(4097).ToArray();
                    if (receipt.SchemaVersion != 1 || receipt.PackageId != descriptor.PackageId || receipt.AppId != descriptor.AppId ||
                        receipt.Version != descriptor.Version || receipt.Platform != "linux" || receipt.Abi != descriptor.Abi ||
                        !HomePackageOriginalInstalledActivationRecord.SafeRelative(receipt.EntrypointRelativePath) ||
                        files is not { Length: > 0 and <= 4096 } || files.Any(value => value is null ||
                            !HomePackageOriginalInstalledActivationRecord.SafeRelative(value.RelativePath) || value.Bytes < 0 ||
                            value.Sha256 is not { Length: 64 } || !value.Sha256.All(Uri.IsHexDigit) ||
                            value.RelativePath == layout.SignedReceiptRelativePath || value.RelativePath == layout.CompletePayloadRelativePath) ||
                        files.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != files.Length ||
                        !files.Any(value => value.RelativePath == receipt.EntrypointRelativePath))
                        throw new InvalidDataException("The exact signed receipt identity and bounded runtime cohort are required.");
                    foreach (var file in files)
                    {
                        var relative = layout.RelativeRoot + "/" + file.RelativePath;
                        // Every executable in the signed runtime cohort must be a native
                        // ELF for this signed ABI; no public executable shell/script.
                        using var held = LinuxOriginalDownloadPaths.OpenAt(stageDirectory, relative, 0x80000UL | 0x800UL);
                        var identity = LinuxOriginalDownloadPaths.ReadIdentity(held);
                        LinuxOriginalDownloadPaths.DemandOwner(held, 0);
                        if (file.RelativePath == receipt.EntrypointRelativePath && (identity.Mode & 0x40) == 0)
                            throw new InvalidDataException("Every signed app entrypoint must already have owner-execute permission.");
                        if ((identity.Mode & 0x49) != 0 || file.RelativePath == receipt.EntrypointRelativePath)
                            DemandElf(held, descriptor.Abi);
                        complete.Add(file with { RelativePath = relative });
                    }
                    complete.Add(new(receiptRelative, receiptBytes.Length, Digest(receiptBytes)));
                    var payloadRelative = layout.RelativeRoot + "/" + layout.CompletePayloadRelativePath;
                    using (var payloadFile = LinuxOriginalDownloadPaths.OpenAt(stageDirectory, payloadRelative, 0x80000UL | 0x800UL))
                    {
                        var payloadIdentity = LinuxOriginalDownloadPaths.ReadIdentity(payloadFile);
                        LinuxOriginalDownloadPaths.DemandOwner(payloadFile, 0);
                        if (!payloadIdentity.IsRegular || payloadIdentity.Links != 1 || (payloadIdentity.Mode & 0x49) != 0 ||
                            payloadIdentity.Size != (ulong)descriptor.PayloadBytes)
                            throw new InvalidDataException("The complete signed payload must be an exact non-executable regular artifact.");
                    }
                    complete.Add(new(payloadRelative, descriptor.PayloadBytes, descriptor.PayloadSha256));
                    verified.Add(new(layout, row, receipt with { Files = Array.AsReadOnly(files) }));
                }
                DemandDependencies(verified, original.Enrollment);
                if (!stageIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(stageDirectory)))
                    throw new IOException("The original private bundle stage changed during signature validation.");
                using var artifacts = LinuxHomePackageArtifactStore.ForVerifiedMachineActivation(parent);
                var entrypoint = verified[0].Layout.RelativeRoot + "/" + verified[0].Receipt.EntrypointRelativePath;
                if (recovery)
                {
                    using var published = artifacts.ValidatePublishedMachineCode(complete, entrypoint, cancellation);
                    if ((LinuxOriginalDownloadPaths.ReadIdentity(published.Directory).Mode & 0x1ff) != 0x1ed)
                        throw new UnauthorizedAccessException("Recovery requires the already-published readable machine-code bundle.");
                    foreach (var value in verified)
                    {
                        var journal = original.Registry.RecentOperations.Single(item => item.PackageId == value.Layout.PackageId);
                        if (journal.UnknownFields is not { } fields || !fields.TryGetValue("linux.original-bootstrap-publication.v1", out var retainedBinding))
                            throw new UnauthorizedAccessException("The original operation has no retained bootstrap publication binding.");
                        var binding = retainedBinding.Deserialize<JournalBinding>(Web);
                        var expected = new JournalBinding(1, parent, request.StageLeaf, Digest(planBytes), value.Layout,
                            value.Row.Descriptor.SignedInstallationReceiptSha256, value.Row.Descriptor.PayloadSha256, original.Enrollment.CatalogueRevision);
                        if (binding != expected) throw new UnauthorizedAccessException("The original operation's parent, stage, plan, signed payload or layout differs; recovery never adopts another bundle.");
                    }
                    observedActive = stagePath;
                    await original.DemandCurrentAsync().ConfigureAwait(false);
                    var allSettled = original.Registry.RecentOperations.All(value => value.State == HomePackageJournalState.Succeeded);
                    var allPending = original.Registry.RecentOperations.All(value => value.State == HomePackageJournalState.Validated);
                    var recoveredPackages = verified.Select(value => InstalledEntry(value, original.Enrollment, stagePath, operation,
                        original.Registry.RecentOperations.Single(item => item.PackageId == value.Layout.PackageId).StartedAtUtc)).ToArray();
                    if (allSettled)
                    {
                        if (original.Registry.Packages.Count != recoveredPackages.Length || recoveredPackages.Any(expected =>
                            !original.Registry.Packages.Any(actual => SameInstalledActivation(actual, expected))))
                            throw new UnauthorizedAccessException("A settled operation must still identify its SAME current installed activation; recovery does not overwrite it.");
                        result = new(request.SigningInputs.OperationId, stagePath, original.Registry.Revision,
                            Array.AsReadOnly(recoveredPackages.Select(value => value.PackageId).ToArray())) { AlreadySettled = true };
                        if (originalRuntime is not null)
                        {
                            var packages = recoveredPackages.Select(value => value.UnknownFields![HomePackageOriginalInstalledActivationRecord.Field]
                                .Deserialize<HomePackageOriginalInstalledActivationRecord>()!).ToArray();
                            var code = new PublishedCode(artifacts, published, Array.AsReadOnly(packages), original.DemandCurrentAsync);
                            try { await originalRuntime(code, cancellation).ConfigureAwait(false); }
                            finally { code.Close(); }
                        }
                        return;
                    }
                    if (originalRuntime is not null)
                        throw new UnauthorizedAccessException("Root runtime requires an already-settled original bootstrap; service startup never performs installer recovery.");
                    if (!allPending || original.Registry.Packages.Count != 0)
                        throw new UnauthorizedAccessException("Mixed or changed package/journal outcomes require explicit owning recovery.");
                    var recovered = await original.SaveAsync(original.Registry with { Packages = recoveredPackages,
                        RecentOperations = original.Registry.RecentOperations.Select(SettledJournal).ToArray() }, original.Registry.Revision, cancellation).ConfigureAwait(false);
                    result = new(request.SigningInputs.OperationId, stagePath, recovered.Revision,
                        Array.AsReadOnly(recoveredPackages.Select(value => value.PackageId).ToArray()));
                    return;
                }
                if (artifacts.ObserveActive() is not null) throw new IOException("Initial publication refuses an existing active bundle.");
                using var stage = artifacts.ValidateStage(request.StageLeaf, complete, entrypoint, cancellation);
                var now = DateTimeOffset.UtcNow;
                var journals = verified.Select(value => new HomePackageJournalEntry("linux.bootstrap:" + operation + ":" + value.Row.Descriptor.PackageId,
                    operation, value.Row.Descriptor.PackageId, "Install", HomePackageJournalState.Validated, now, null,
                    "LinuxBootstrapValidated", false, false, ["enrolled-publisher", "signed-receipt", "complete-payload"], [], [], [])
                { UnknownFields = new(StringComparer.Ordinal) { ["linux.original-bootstrap-publication.v1"] = JsonSerializer.SerializeToElement(new JournalBinding(1,
                    parent, request.StageLeaf, Digest(planBytes), value.Layout, value.Row.Descriptor.SignedInstallationReceiptSha256,
                    value.Row.Descriptor.PayloadSha256, original.Enrollment.CatalogueRevision)) } }).ToArray();
                // A failed/unknown acknowledgement is never permission to retry effects.
                journalAttempted = true;
                var pending = await original.SaveAsync(original.Registry with { RecentOperations = journals }, original.Registry.Revision, cancellation).ConfigureAwait(false);
                await original.DemandCurrentAsync().ConfigureAwait(false);
                artifacts.MakeVerifiedCodeReadable(stage);
                await original.DemandCurrentAsync().ConfigureAwait(false);
                // Last safe cancellation boundary is inside Publish, before rename.
                var publication = artifacts.Publish(stage, null, cancellation);
                observedActive = publication.ActivePath;
                var packages = verified.Select(value => InstalledEntry(value, original.Enrollment, publication.ActivePath, operation, now)).ToArray();
                var settled = journals.Select(SettledJournal).ToArray();
                // Do not cancel settlement after the filesystem namespace changed.
                var written = await original.SaveAsync(pending with { Packages = packages, RecentOperations = settled }, pending.Revision, CancellationToken.None).ConfigureAwait(false);
                result = new(request.SigningInputs.OperationId, publication.ActivePath, written.Revision,
                    Array.AsReadOnly(packages.Select(value => value.PackageId).ToArray()));
            }, token).ConfigureAwait(false);
            return result ?? throw new InvalidOperationException("The original activation operation did not complete.");
        }
        catch (Exception cause) when (journalAttempted && originalRuntime is null)
        {
            if (cause is LinuxHomePackageArtifactStore.PublicationUncertainException uncertain)
                observedActive = uncertain.ObservedPublication.ActivePath;
            throw new ActivationUnconfirmedException(request.SigningInputs.OperationId, observedActive, cause);
        }
    }

    private static HomePackageJournalEntry SettledJournal(HomePackageJournalEntry value) => value with {
        State = HomePackageJournalState.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow,
        ResultCode = "LinuxBootstrapActivatedRuntimeUnavailable", SucceededSteps = value.SucceededSteps.Concat(["atomic-bundle-publication"]).ToArray(),
        SkippedSteps = ["root-service-not-started", "installed-peer-not-established"] };
    private static bool SameInstalledActivation(HomePackageDatabaseEntry actual, HomePackageDatabaseEntry expected)
        // Every original field, including integrity/dependencies/revision, must still
        // match; recovery never overwrites a later owning policy decision.
        => JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected);
    private static HomePackageDatabaseEntry InstalledEntry(Verified value, HomePackageOriginalPublisherEnrollmentRecord enrollment,
        string activePath, string operation, DateTimeOffset verifiedAt)
    {
        var descriptor = value.Row.Descriptor; var root = Path.Combine(activePath, value.Layout.RelativeRoot);
        var activation = new HomePackageOriginalInstalledActivationRecord(1, descriptor.PackageId, descriptor.AppId, descriptor.Version,
            descriptor.Platform, descriptor.Abi, enrollment.CatalogueRevision, Convert.ToBase64String(value.Row.Signed),
            Convert.ToBase64String(value.Row.Payload), root, Path.Combine(root, value.Layout.SignedReceiptRelativePath),
            Path.Combine(root, value.Layout.CompletePayloadRelativePath), value.Receipt.EntrypointRelativePath, value.Receipt.Files, operation);
        return new(descriptor.PackageId, descriptor.AppId, descriptor.AppId, null, descriptor.Version, descriptor.Version,
            descriptor.Channel, HomePackageInstallState.Installed, HomePackageCompatibility.HomeServiceUnavailable,
            "The signed code bundle is installed; the actual Root service, authenticated installed peer and controlled launch are not established.",
            descriptor.Dependencies, [new(descriptor.Version, descriptor.PayloadSha256, enrollment.PublisherCertificateSha256,
                HomePackageIntegrityState.Verified, verifiedAt)], null, [], true, 1)
        { UnknownFields = new(StringComparer.Ordinal) { [HomePackageOriginalInstalledActivationRecord.Field] = JsonSerializer.SerializeToElement(activation) } };
    }
    private static void DemandDependencies(IReadOnlyList<Verified> packages, HomePackageOriginalPublisherEnrollmentRecord enrollment)
    {
        var selected = packages.ToDictionary(value => value.Row.Descriptor.PackageId, StringComparer.Ordinal);
        foreach (var item in packages)
        {
            var descriptor = item.Row.Descriptor;
            if (descriptor.ComponentClass is not ("mandatory-shared-core" or "optional-app" or "app-required-dependency" or "optional-feature-dependency") ||
                (descriptor.PackageId == enrollment.HomePackageId || descriptor.PackageId == enrollment.RootPackageId) && descriptor.ComponentClass != "mandatory-shared-core" ||
                !HomePackageOriginalVersionChecks.TrySatisfy(descriptor.Version, new(descriptor.PackageId), out var valid) || !valid)
                throw new InvalidDataException("The selected package requires the maintained component class and strict version policy.");
            foreach (var dependency in descriptor.Dependencies)
                if (!selected.TryGetValue(dependency.PackageId, out var required) ||
                    !HomePackageOriginalVersionChecks.TrySatisfy(required.Row.Descriptor.Version, dependency, out var satisfies) || !satisfies)
                    throw new InvalidDataException("The enrolled selection does not satisfy the complete signed dependency closure.");
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("The signed dependency graph contains a cycle.");
            foreach (var dependency in selected[id].Row.Descriptor.Dependencies) Visit(dependency.PackageId);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var id in selected.Keys) Visit(id);
    }
    private static byte[] ReadBounded(SafeFileHandle directory, string path, string relative, int maximum)
    {
        using var file = LinuxOriginalDownloadPaths.OpenAt(directory, relative, 0x80000UL | 0x800UL);
        var before = LinuxOriginalDownloadPaths.ReadIdentity(file);
        LinuxOriginalDownloadPaths.DemandOwner(file, 0);
        if (!before.IsRegular || before.Links != 1 || before.Size is 0 || before.Size > (ulong)maximum || (before.Mode & 0x49) != 0)
            throw new InvalidDataException("Administrative plans and signed receipts must be bounded, non-executable single-link regular files.");
        var fullPath = Path.Combine(path, relative);
        LinuxOriginalDownloadPaths.DemandPath(file, fullPath, directory: false);
        var bytes = new byte[(int)before.Size]; var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(file, bytes.AsSpan(offset), offset);
            if (read == 0) throw new EndOfStreamException("The original signed input was truncated.");
            offset += read;
        }
        if (!before.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(file))) throw new IOException("The original signed input changed.");
        LinuxOriginalDownloadPaths.DemandPath(file, fullPath, directory: false); return bytes;
    }
    private static void DemandElf(SafeFileHandle held, string abi)
    {
        Span<byte> header = stackalloc byte[20];
        if (RandomAccess.Read(held, header, 0) != header.Length || !header[..4].SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }) ||
            header[4] != 2 || header[5] != 1 || header[6] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[16..]) is not (2 or 3) ||
            BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) != (abi is "arm64" or "linux-arm64" ? 183 : abi is "x64" or "linux-x64" ? 62 : -1))
            throw new InvalidDataException("The signed runtime executable must be native ELF64 for its exact supported Linux ABI; scripts and unknown ABIs are refused.");
    }
    private static bool Contains(string parent, string path) => path == parent || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Haven.Infrastructure.Native.Windows;

namespace Haven.Infrastructure.Native.Linux;

// Detached command observations only. The authenticated controlled Home channel,
// private retained operation and actual protected canonical writer own admission.
internal static class LinuxHomePackageRemovalWire
{
    internal sealed record Open(string OperationId, HomePackageActionRequest Action, AuthenticatedResourceActor Actor,
        HomeNativeInstalledPeer InstalledCaller, HomeNativeObservedPeer ObservedPeer, string SessionId,
        string OriginalApprovalRequestId, string ReviewedArgumentsSha256, long RegistryRevision,
        long PackageEntryRevision, string CatalogueRevision, string SignedDescriptorSha256);
    internal sealed record Outcome(HomePackageActionResult Result, HomePackageDatabaseEntry Package);
    internal sealed record Proof(int SchemaVersion, string BootstrapOperationId, string OperationId, string IdempotencyKey,
        string PackageId, string AppId, string CatalogueRevision, string OriginalEntrySha256, string ActivationSha256,
        string SignedDescriptorSha256, string ReviewedArgumentsSha256, string OriginalApprovalRequestId,
        AuthenticatedResourceActor OriginalActor, string OriginalSessionId, Guid OriginalHomeApplicationId);
    internal const string ProofField = "linux.original-optional-removal.v1";
    internal const string SuccessCode = "HomePackages.LinuxOptionalAppRemoved";
    internal static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static HomePackageArtifactDescriptor Descriptor(HomePackageOriginalInstalledActivationRecord activation)
        => HomePackageOriginalArtifactDescriptorParser.Parse(Convert.FromBase64String(activation.SignedDescriptorBase64),
            Convert.FromBase64String(activation.DescriptorPayloadBase64), activation.CatalogueRevision,
            new(activation.PackageId, HomePackageAction.Uninstall, "linux.root.removal.declaration", ExpectedRevision: activation.CatalogueRevision)).Descriptor;
    internal static HomePackageDatabaseEntry RemovedEntry(HomePackageDatabaseEntry original, Proof proof)
    {
        var fields = original.UnknownFields?.ToDictionary(value => value.Key, value => value.Value.Clone(), StringComparer.Ordinal)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        fields[ProofField] = JsonSerializer.SerializeToElement(proof);
        return original with { InstalledVersion = null, InstallationState = HomePackageInstallState.Available,
            Compatibility = HomePackageCompatibility.HomeServiceUnavailable,
            CompatibilityReason = "Removed; owned artifacts and immutable signed rollback bytes are retained.",
            LastKnownGoodVersion = original.InstalledVersion,
            RetainedRollbackVersions = original.RetainedRollbackVersions.Append(original.InstalledVersion!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Revision = checked(original.Revision + 1), UnknownFields = fields };
    }
    // This reconciles only source-proven terminal optional removal. The initial
    // signed code cohort is still verified completely by the existing bootstrap.
    internal static void DemandOriginalCanonicalOutcomes(HomePackageDatabaseSnapshot current,
        IReadOnlyList<HomePackageDatabaseEntry> originalInstalled, string originalHomePackageId, string bootstrapOperation)
    {
        if (HomePackageDatabase.ValidateOriginalProtectedSnapshot(current) is not null || current.Packages.Count != originalInstalled.Count)
            throw new UnauthorizedAccessException("The same complete canonical package identity set is required.");
        foreach (var original in originalInstalled)
        {
            var actual = current.Packages.SingleOrDefault(value => value.PackageId == original.PackageId)
                ?? throw new UnauthorizedAccessException("Canonical package identities cannot disappear without their owning removal record.");
            if (Digest(original) == Digest(actual)) continue;
            var activation = original.UnknownFields![HomePackageOriginalInstalledActivationRecord.Field].Deserialize<HomePackageOriginalInstalledActivationRecord>()!;
            var descriptor = Descriptor(activation);
            var proof = actual.UnknownFields is { } fields && fields.TryGetValue(ProofField, out var raw)
                ? raw.Deserialize<Proof>() : null;
            if (descriptor.ComponentClass != "optional-app" || original.PackageId == originalHomePackageId || proof is not { SchemaVersion: 1 } ||
                proof.BootstrapOperationId != bootstrapOperation || proof.PackageId != original.PackageId || proof.AppId != original.AppId ||
                proof.CatalogueRevision != activation.CatalogueRevision || proof.OriginalEntrySha256 != Digest(original) ||
                proof.ActivationSha256 != Digest(activation) || proof.SignedDescriptorSha256 != Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(activation.SignedDescriptorBase64))) ||
                !Hash(proof.ReviewedArgumentsSha256) || string.IsNullOrWhiteSpace(proof.OriginalApprovalRequestId) ||
                proof.OriginalActor is not { AccountId: null, OrganisationId: null } || string.IsNullOrWhiteSpace(proof.OriginalActor.ActorId) ||
                string.IsNullOrWhiteSpace(proof.OriginalActor.ProfileId) || string.IsNullOrWhiteSpace(proof.OriginalActor.AuthenticationRevision) ||
                string.IsNullOrWhiteSpace(proof.OperationId) || string.IsNullOrWhiteSpace(proof.IdempotencyKey) || string.IsNullOrWhiteSpace(proof.OriginalSessionId) || proof.OriginalHomeApplicationId == Guid.Empty ||
                Digest(actual) != Digest(RemovedEntry(original, proof)))
                throw new UnauthorizedAccessException("A changed optional package requires its exact original protected removal provenance.");
            var journal = current.RecentOperations.SingleOrDefault(value => value.OperationId == proof.OperationId);
            if (journal is null || journal.PackageId != original.PackageId || journal.IdempotencyKey != proof.IdempotencyKey ||
                journal.Action != nameof(HomePackageAction.Uninstall) || journal.State != HomePackageJournalState.Succeeded ||
                journal.CompletedAtUtc is null || journal.ResultCode != SuccessCode || !journal.PreviousKnownGoodVersionRetained)
                throw new UnauthorizedAccessException("Only the original succeeded canonical removal journal can confirm unregistration.");
            var beforeRemoval = current with { Packages = current.Packages.Select(value => value.PackageId == original.PackageId ? original : value).ToArray() };
            if (HomePackageOriginalRemovalChecks.Evaluate(beforeRemoval, descriptor, originalHomePackageId, proof.OperationId) is not null)
                throw new UnauthorizedAccessException("The original mandatory/dependency/artifact-preservation removal policy is no longer satisfied.");
        }
        foreach (var journal in current.RecentOperations.Where(value => value.OperationId != bootstrapOperation))
        {
            var original = originalInstalled.SingleOrDefault(value => value.PackageId == journal.PackageId);
            if (original is null || journal.Action != nameof(HomePackageAction.Uninstall) ||
                journal.State is not (HomePackageJournalState.Pending or HomePackageJournalState.Succeeded or HomePackageJournalState.Failed or HomePackageJournalState.OutcomeUnknown) ||
                Descriptor(original.UnknownFields![HomePackageOriginalInstalledActivationRecord.Field].Deserialize<HomePackageOriginalInstalledActivationRecord>()!).ComponentClass != "optional-app")
                throw new UnauthorizedAccessException("Only original optional removal lifecycle entries may extend this bootstrap.");
            if (journal.State == HomePackageJournalState.Succeeded && !current.Packages.Any(value => value.PackageId == journal.PackageId &&
                value.UnknownFields is { } fields && fields.TryGetValue(ProofField, out var raw) && raw.Deserialize<Proof>()?.OperationId == journal.OperationId))
                throw new UnauthorizedAccessException("An unproven successful journal cannot broaden installed-package trust.");
        }
    }
    internal static bool IsAvailable(HomePackageDatabaseSnapshot registry, string package)
        => registry.Packages.Any(value => value.PackageId == package && value.InstallationState == HomePackageInstallState.Installed && value.InstalledVersion is not null) &&
            !registry.RecentOperations.Any(value => value.PackageId == package && value.State is HomePackageJournalState.Pending or HomePackageJournalState.Staged or HomePackageJournalState.Validated or
                HomePackageJournalState.Activating or HomePackageJournalState.OutcomeUnknown);
    private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed partial class LinuxHomeRootServiceRuntime
{
    private sealed class OriginalPackageRemovalController(
        LinuxHomeBootstrapActivationOwner.PublishedCode code, LinuxHomeRegisteredRootServiceContext kernel,
        LinuxHomeRegisteredRootServiceContext.InteractiveUser user, LinuxHomeOriginalControlledProcess originalHome,
        Func<NativeWindowsHomeRootWire.Listening?> listening, OriginalSelectedApplications selected)
    {
        private sealed class Operation(LinuxHomePackageRemovalWire.Open request, HomePackageDatabaseSnapshot before,
            HomePackageDatabaseEntry original, HomePackageOriginalInstalledActivationRecord activation)
        {
            internal readonly LinuxHomePackageRemovalWire.Open Request = request;
            internal readonly HomePackageDatabaseSnapshot Before = before;
            internal readonly HomePackageDatabaseEntry Original = original;
            internal readonly HomePackageOriginalInstalledActivationRecord Activation = activation;
            internal HomePackageDatabaseSnapshot? Reserved;
            internal LinuxHomePackageRemovalWire.Outcome? Outcome;
            internal Task<LinuxHomePackageRemovalWire.Outcome>? Effect;
            internal bool Settled, Closed;
            internal Exception? EffectFailure;
        }
        private readonly Dictionary<string, Operation> _operations = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private void DemandOriginalHome()
        { kernel.DemandOriginalCurrent(user); originalHome.DemandOriginalCurrent(); }
        internal async Task<HomeStateReadResult> ReadOriginalStateAsync(CancellationToken token)
        {
            DemandOriginalHome();
            var read = await code.ReadOriginalPackageStateAsync(token).ConfigureAwait(false);
            if (read.IsSuccess) ValidateCurrent(Parse(read.State!));
            DemandOriginalHome(); return read;
        }
        private static HomePackageDatabaseSnapshot Parse(HomeCoreStoredState state)
        {
            var record = state.Records.SingleOrDefault(value => value.RecordId == HomePackageDatabase.RecordId);
            if (record is null || record.RecordType != HomePackageDatabase.RecordType || record.Scope != HomeDataScope.DeviceLocal ||
                record.Authority != HomeRecordAuthority.LocalCanonical || record.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion)
                throw new UnauthorizedAccessException("The original canonical package record is unavailable.");
            var value = record.Payload.Deserialize<HomePackageDatabaseSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("The original canonical package payload is absent.");
            if (value.Revision != record.Revision || HomePackageDatabase.ValidateOriginalProtectedSnapshot(value) is not null)
                throw new InvalidDataException("The original canonical package payload/revision is invalid.");
            return value;
        }
        private void ValidateCurrent(HomePackageDatabaseSnapshot current)
            => LinuxHomePackageRemovalWire.DemandOriginalCanonicalOutcomes(current, code.OriginalInstalledEntries,
                code.Packages.Single(value => value.AppId == "home").PackageId,
                code.Packages.Single(value => value.AppId == "root").OriginalRootOperationId);
        internal async Task<bool> IsOriginalPackageAvailableAsync(string package, CancellationToken token)
        {
            var read = await ReadOriginalStateAsync(token).ConfigureAwait(false);
            return read.IsSuccess && LinuxHomePackageRemovalWire.IsAvailable(Parse(read.State!), package);
        }
        internal async Task<LinuxHomeRootControlWire.Response?> HandleAsync(LinuxHomeRootControlWire.Request message, CancellationToken token)
        {
            if (message.Command == "read-package-state")
            {
                var reply = new LinuxHomeRootControlWire.Response(1, message.CorrelationId, true, null) { PackageState = await ReadOriginalStateAsync(token).ConfigureAwait(false) };
                // Reserve room for the later SAME-record CAS envelope. An oversized
                // catalogue makes package mutations unavailable; it must not tear
                // down the existing Home/Root channel or grow its frame bound.
                return JsonSerializer.SerializeToUtf8Bytes(reply).Length <= 2 * 1024 * 1024 - 64 * 1024 ? reply
                    : new(1, message.CorrelationId, true, "HomePackages.OriginalRegistryExceedsControlBound");
            }
            if (message.Command is not ("open-package-removal" or "write-package-state" or "execute-package-removal" or "observe-package-removal" or "close-package-removal")) return null;
            DemandOriginalHome();
            if (message.Command == "open-package-removal")
            {
                var request = message.PackageRemoval ?? throw new UnauthorizedAccessException("The original package removal request is absent.");
                var actualListening = listening();
                if (actualListening is null || request.Actor != actualListening.Actor || request.Actor.AccountId is not null || request.Actor.OrganisationId is not null ||
                    request.Action.Action != HomePackageAction.Uninstall || request.Action.RequestedVersion is not null || request.Action.RequestedChannel is not null ||
                    request.ObservedPeer.ProcessId != originalHome.ProcessId ||
                    request.ObservedPeer.OperatingSystemPrincipalId != user.OperatingSystemPrincipalId || request.InstalledCaller.AppId != "home" ||
                    request.InstalledCaller.InstalledApplicationId != actualListening.InstalledApplicationId ||
                    request.InstalledCaller.InstallationRevision != actualListening.InstallationRevision ||
                    request.InstalledCaller.ExecutableIdentity != originalHome.ExecutableIdentity ||
                    !request.InstalledCaller.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole) ||
                    !request.InstalledCaller.AllowedServiceIds.Contains("home.core") || !Text(request.OperationId) || !Text(request.Action.IdempotencyKey) ||
                    !Text(request.SessionId) || !Text(request.OriginalApprovalRequestId) || !Hash(request.ReviewedArgumentsSha256))
                    throw new UnauthorizedAccessException("Only the SAME actual controlled Home/session/individual review can open removal.");
                var read = await ReadOriginalStateAsync(token).ConfigureAwait(false);
                var before = Parse(read.State ?? throw new InvalidDataException("The original package registry cannot be read."));
                var original = before.Packages.SingleOrDefault(value => value.PackageId == request.Action.PackageId)
                    ?? throw new UnauthorizedAccessException("The original installed package is absent.");
                var activation = code.Packages.SingleOrDefault(value => value.PackageId == original.PackageId)
                    ?? throw new UnauthorizedAccessException("The package is not in the same retained signed cohort.");
                var descriptor = LinuxHomePackageRemovalWire.Descriptor(activation);
                if (descriptor.ComponentClass != "optional-app" || before.Revision != request.RegistryRevision || original.Revision != request.PackageEntryRevision ||
                    request.CatalogueRevision != activation.CatalogueRevision || request.Action.ExpectedRevision != activation.CatalogueRevision ||
                    request.SignedDescriptorSha256 != Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(activation.SignedDescriptorBase64))) ||
                    HomePackageOriginalRemovalChecks.Evaluate(before, descriptor, code.Packages.Single(value => value.AppId == "home").PackageId) is not null)
                    throw new UnauthorizedAccessException("The same optional package/revision/dependency policy is required.");
                lock (_gate)
                {
                    if (_operations.TryGetValue(request.OperationId, out var old))
                    { if (LinuxHomePackageRemovalWire.Digest(old.Request) != LinuxHomePackageRemovalWire.Digest(request)) throw new UnauthorizedAccessException("The original removal operation cannot be rebound."); }
                    else
                    {
                        if (_operations.Count >= 64 || _operations.Values.Any(value => !value.Closed && !value.Settled))
                            throw new InvalidOperationException("Settle the original retained Root package operation first.");
                        _operations.Add(request.OperationId, new(request, before, original, activation));
                    }
                }
                return new(1, message.CorrelationId, true, null) { PackageOperationId = request.OperationId };
            }
            Operation operation;
            lock (_gate) operation = message.PackageOperationId is { } id && _operations.TryGetValue(id, out var original)
                ? original : throw new UnauthorizedAccessException("The same privately retained Root removal operation is required.");
            if (operation.Closed) throw new UnauthorizedAccessException("A closed original removal cannot execute again.");
            if (message.Command == "write-package-state")
            {
                var record = message.PackageRecord ?? throw new InvalidDataException("The original package record is absent.");
                var current = Parse((await ReadOriginalStateAsync(token).ConfigureAwait(false)).State!);
                var next = Parse(new(FileHomeCoreStateStore.CurrentSchemaVersion, 0, [record]));
                if (current.Revision != message.ExpectedPackageRevision || next.Revision != current.Revision + 1)
                    throw new UnauthorizedAccessException("The original canonical package CAS changed.");
                var expected = operation.Reserved is null ? Reservation(operation, current, next) : Settlement(operation, current, next);
                // The canonical DB owns its update timestamp and ordinal row ordering.
                expected = expected with { Revision = next.Revision, UpdatedAtUtc = next.UpdatedAtUtc,
                    Packages = expected.Packages.OrderBy(value => value.PackageId, StringComparer.Ordinal).ToArray() };
                if (LinuxHomePackageRemovalWire.Digest(next) != LinuxHomePackageRemovalWire.Digest(expected))
                    throw new UnauthorizedAccessException("The client cannot write unrelated package rows, publisher enrollment or journal state.");
                var written = await code.WriteOriginalPackageStateAsync(record, current.Revision, new OriginalWriteGuard(this, operation), token).ConfigureAwait(false);
                if (written.IsSuccess)
                {
                    if (operation.Reserved is null) operation.Reserved = next;
                    else operation.Settled = true;
                }
                return new(1, message.CorrelationId, true, null) { PackageOperationId = operation.Request.OperationId, PackageWrite = written };
            }
            if (message.Command == "execute-package-removal")
            {
                Task<LinuxHomePackageRemovalWire.Outcome> effect;
                lock (_gate)
                {
                    if (operation.Reserved is null) throw new UnauthorizedAccessException("The same durable canonical reservation must precede its removal effect.");
                    effect = operation.Effect ??= Execute(operation, token);
                }
                operation.Outcome = await effect.ConfigureAwait(false);
            }
            else if (message.Command == "close-package-removal") operation.Closed = true;
            return new(1, message.CorrelationId, true, null) { PackageOperationId = operation.Request.OperationId, PackageRemovalOutcome = operation.Outcome };
        }
        private static HomePackageDatabaseSnapshot Reservation(Operation operation, HomePackageDatabaseSnapshot current, HomePackageDatabaseSnapshot proposed)
        {
            if (LinuxHomePackageRemovalWire.Digest(current) != LinuxHomePackageRemovalWire.Digest(operation.Before) ||
                current.RecentOperations.Count >= HomePackageDatabase.MaximumJournalEntries)
                throw new UnauthorizedAccessException("The original Root reservation snapshot changed.");
            var candidate = proposed.RecentOperations.SingleOrDefault(value => value.OperationId == operation.Request.OperationId)
                ?? throw new UnauthorizedAccessException("The same exact original Pending journal is required.");
            var expected = new HomePackageJournalEntry(operation.Request.Action.IdempotencyKey, operation.Request.OperationId,
                operation.Request.Action.PackageId, nameof(HomePackageAction.Uninstall), HomePackageJournalState.Pending,
                candidate.StartedAtUtc, null, "HomePackages.OriginalReserved", false, operation.Original.LastKnownGoodVersion is not null,
                [], [], [], []);
            if (candidate.StartedAtUtc == default || candidate.StartedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
                LinuxHomePackageRemovalWire.Digest(candidate) != LinuxHomePackageRemovalWire.Digest(expected))
                throw new UnauthorizedAccessException("The requested journal is not the original bounded pending removal intent.");
            return current with { RecentOperations = current.RecentOperations.Append(expected).ToArray() };
        }
        private static HomePackageDatabaseSnapshot Settlement(Operation operation, HomePackageDatabaseSnapshot current, HomePackageDatabaseSnapshot proposed)
        {
            if (operation.Outcome is null) throw new UnauthorizedAccessException("Unknown effects cannot be fabricated into canonical completion.");
            var journal = current.RecentOperations.Single(value => value.OperationId == operation.Request.OperationId);
            var candidate = proposed.RecentOperations.Single(value => value.OperationId == operation.Request.OperationId);
            var outcome = operation.Outcome.Result;
            if (candidate.CompletedAtUtc is not { } completed || completed < journal.StartedAtUtc || completed > DateTimeOffset.UtcNow.AddMinutes(1))
                throw new UnauthorizedAccessException("The terminal original removal timestamp is unavailable.");
            return current with { Packages = current.Packages.Select(value => value.PackageId == operation.Request.Action.PackageId ? operation.Outcome.Package : value).ToArray(),
                RecentOperations = current.RecentOperations.Select(value => value.OperationId == journal.OperationId ? value with {
                    State = outcome.State == HomePackageOperationState.Succeeded ? HomePackageJournalState.Succeeded : HomePackageJournalState.Failed,
                    CompletedAtUtc = completed, ResultCode = outcome.Code, Retryable = false,
                    PreviousKnownGoodVersionRetained = outcome.PreviousKnownGoodVersionRetained, SucceededSteps = outcome.SucceededSteps,
                    FailedSteps = outcome.FailedSteps, SkippedSteps = outcome.SkippedSteps, RolledBackSteps = outcome.RolledBackSteps } : value).ToArray() };
        }
        private async Task<LinuxHomePackageRemovalWire.Outcome> Execute(Operation operation, CancellationToken token)
        {
            try
            {
                DemandOriginalHome(); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                var current = Parse((await ReadOriginalStateAsync(token).ConfigureAwait(false)).State!);
                var homeId = code.Packages.Single(value => value.AppId == "home").PackageId;
                if (HomePackageOriginalRemovalChecks.Evaluate(current, LinuxHomePackageRemovalWire.Descriptor(operation.Activation), homeId, operation.Request.OperationId) is not null)
                    throw new UnauthorizedAccessException("The original optional removal policy changed before effect.");
                if (selected.HasLiveOriginalPackageChild(operation.Request.Action.PackageId))
                    return new(Result(operation, HomePackageOperationState.Rejected, "HomePackages.AppStillRunning", "Close this app before removing it."), operation.Original);
                await selected.RetireOriginalPackageChildrenAsync(operation.Request.Action.PackageId).ConfigureAwait(false);
                DemandOriginalHome(); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                var source = operation.Request;
                var proof = new LinuxHomePackageRemovalWire.Proof(1, operation.Activation.OriginalRootOperationId,
                    source.OperationId, source.Action.IdempotencyKey, source.Action.PackageId, operation.Activation.AppId,
                    source.CatalogueRevision, LinuxHomePackageRemovalWire.Digest(operation.Original), LinuxHomePackageRemovalWire.Digest(operation.Activation),
                    source.SignedDescriptorSha256, source.ReviewedArgumentsSha256, source.OriginalApprovalRequestId, source.Actor, source.SessionId, source.InstalledCaller.InstalledApplicationId);
                return new(Result(operation, HomePackageOperationState.Succeeded, LinuxHomePackageRemovalWire.SuccessCode,
                    "App removed. Its files and signed rollback copy are retained."), LinuxHomePackageRemovalWire.RemovedEntry(operation.Original, proof));
            }
            catch (Exception cause) { operation.EffectFailure = cause; throw; }
        }
        private static HomePackageActionResult Result(Operation operation, HomePackageOperationState state, string code, string message)
            => new(operation.Request.OperationId, operation.Request.Action.PackageId, HomePackageAction.Uninstall, state, code, message,
                false, true, true, state == HomePackageOperationState.Succeeded ? ["OriginalControlledChildrenRetired", "ImmutableRollbackCohortRetained"] : [], [], [], []);
        private sealed class OriginalWriteGuard(OriginalPackageRemovalController owner, Operation operation) : IHomeStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor, HomeStateCommitPhase phase, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); owner.DemandOriginalHome();
                lock (owner._gate) return ValueTask.FromResult(!operation.Closed && operation.EffectFailure is null &&
                    owner._operations.TryGetValue(operation.Request.OperationId, out var actual) && ReferenceEquals(actual, operation));
            }
        }
        internal async Task CloseAndDrainAsync()
        {
            Operation[] all; lock (_gate) all = _operations.Values.ToArray();
            var failures = new List<Exception>();
            foreach (var operation in all)
                if (operation.Effect is { } effect) try { await effect.ConfigureAwait(false); } catch (Exception cause) { failures.Add(effect.Exception ?? cause); }
            lock (_gate) foreach (var operation in all) operation.Closed = true;
            if (failures.Count != 0) throw new AggregateException("Original Root package effects retained actual failures.", failures);
        }
        private static bool Text(string value) => value is { Length: > 0 and <= 1024 } && !value.Any(char.IsControl);
        private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}

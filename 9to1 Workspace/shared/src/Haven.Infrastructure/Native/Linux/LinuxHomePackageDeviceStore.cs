using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class LinuxHomeRootClient
{
    // Remote view of ONE actual canonical record, not another registry/database.
    // The Root endpoint independently validates the exact permitted transition and
    // commits through its existing protected writer and actual administrative actor.
    private sealed class OriginalPackageDeviceStore(LinuxHomeRootClient owner) : IHomeCoreStateStore
    {
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default)
            => owner.Admit(body => body(), _ => { }, async (source, caller) => {
                if (owner._packageLifecycleVersion != 1) throw new PlatformNotSupportedException("The actual Root package lifecycle endpoint is unavailable.");
                var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "read-package-state"), caller).ConfigureAwait(false);
                return response.PackageState ?? (response.Reason == "HomePackages.OriginalRegistryExceedsControlBound"
                    ? HomeStateReadResult.Failed(new(HomeCoreErrorCode.HomeStateIncompatible,
                        "The original package catalogue exceeds the supported control bound.", HomePackageDatabase.RecordId, false))
                    : throw new InvalidDataException("The actual Root did not return its canonical package state."));
            }, token);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken token = default)
            => Task.FromResult(HomeStateWriteResult.Failed(new(HomeCoreErrorCode.PermissionDenied,
                "The canonical Root package record requires its original reviewed mutation guard.", HomePackageDatabase.RecordId, false)));
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(guard);
            var captured = record with { Payload = record.Payload.Clone() };
            return owner.Admit(body => body(), _ => { }, async (source, caller) => {
                OriginalPackageMutation mutation;
                lock (owner._gate) mutation = owner._currentPackageMutation
                    ?? throw new UnauthorizedAccessException("The same source-owned Root package mutation is unavailable.");
                mutation.DemandOriginalForWrite(actor);
                if (captured.RecordId != HomePackageDatabase.RecordId || captured.RecordType != HomePackageDatabase.RecordType ||
                    captured.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion || captured.Scope != HomeDataScope.DeviceLocal ||
                    captured.Authority != HomeRecordAuthority.LocalCanonical || captured.Revision != checked(expectedRecordRevision + 1))
                    throw new UnauthorizedAccessException("Only the original canonical package record and exact CAS may be written.");
                var read = await source.Read(() => ReadAsync(caller)).ConfigureAwait(false);
                var before = read.State ?? throw new UnauthorizedAccessException("The actual Root package state is unavailable.");
                if (!await guard.CheckAsync(before, actor, HomeStateCommitPhase.Admission, caller).ConfigureAwait(false))
                    return Denied();
                mutation.DemandOriginalForWrite(actor);
                var response = await owner.Exchange(source, new(1, Guid.NewGuid(), "write-package-state") {
                    PackageOperationId = mutation.OriginalRequest.OperationId, PackageRecord = captured, ExpectedPackageRevision = expectedRecordRevision
                }, caller).ConfigureAwait(false);
                var result = response.PackageWrite ?? throw new InvalidDataException("The original Root package CAS has no owning result.");
                if (result.IsSuccess)
                {
                    var written = result.State ?? throw new InvalidDataException("The original Root package writer returned no canonical state.");
                    mutation.DemandOriginalForWrite(actor);
                    if (!await guard.CheckAsync(written, actor, HomeStateCommitPhase.Publication, caller).ConfigureAwait(false))
                        throw new IOException("The actual Root package write occurred but original result publication was not confirmed.");
                }
                return result;
            }, token);
        }
        private static HomeStateWriteResult Denied() => HomeStateWriteResult.Failed(new(HomeCoreErrorCode.PermissionDenied,
            "The original reviewed Root package mutation is no longer current.", HomePackageDatabase.RecordId, false));
    }
}

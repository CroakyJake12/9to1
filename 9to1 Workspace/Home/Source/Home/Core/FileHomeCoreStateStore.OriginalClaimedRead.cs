using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class FileHomeCoreStateStore
{
    /// <summary>A finite currentness observation for this SAME host's individual claimed READ.
    /// No lease, profile, claim or store path is issued. Never wait/reenter another Home writer;
    /// refusal is recoverable by a fresh owner read. The store/process locks stay inside this
    /// callback and are released before any artifact read or native operation.</summary>
    public void DemandOriginalClaimedReadCurrent(HomeResourceOperationBroker sameBroker,
        HomeLocalProfileIdentity sameProfiles, HomeClaimedResourceAttestation sameAttestation,
        AuthenticatedResourceActor sameActor, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask)
    {
        ArgumentNullException.ThrowIfNull(sameBroker); ArgumentNullException.ThrowIfNull(sameProfiles);
        ArgumentNullException.ThrowIfNull(sameAttestation); ArgumentNullException.ThrowIfNull(sameActor);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (!sameProfiles.IsBoundToStore(this) || !sameBroker.IsBoundToLocalCommitStore(this) ||
            sameAttestation.Actor != sameActor || sameAttestation.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            binding.Scopes.Count == 0 || binding.Scopes.Any(scope => scope.Access != ResourceAccess.Read))
            throw new UnauthorizedAccessException("SAME claimed original Home READ/store/profile is required.");
        if (!_gate.Wait(0)) throw new InvalidOperationException("The original Home read fence is busy; retry after the current writer settles.");
        try
        {
            // Reuse the owning store's exact .lock file and FileShare.None convention. No
            // directory creation, file adoption, chmod or asynchronous retry occurs here.
            using var process = new FileStream(_path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1);
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024);
            if (stream.Length is <= 0 or > 32 * 1024 * 1024)
                throw new InvalidDataException("The original Home current-read state exceeds its finite observation bound.");
            var state = JsonSerializer.Deserialize<HomeCoreStoredState>(stream, JsonOptions)
                ?? throw new InvalidDataException("The original Home current-read state is absent.");
            if (state.SchemaVersion != CurrentSchemaVersion || state.Revision < 0 || state.Records is null ||
                state.Records.Any(record => record is null || ValidateRecord(record) is not null) ||
                state.Records.Select(record => record.RecordId).Distinct(StringComparer.Ordinal).Count() != state.Records.Count)
                throw new InvalidDataException("The original Home current-read state is incompatible or invalid.");
            var check = sameProfiles.CheckAsync(state, sameActor, HomeStateCommitPhase.Publication, originalSynchronousScope, retainOriginalTask, CancellationToken.None).AsTask();
            retainOriginalTask(check); // Own an incomplete/faulted original before refusing; never block on it.
            if (!check.IsCompleted) throw new UnauthorizedAccessException("A pending principal check cannot certify synchronous native use.");
            if (!check.GetAwaiter().GetResult() || !sameBroker.IsClaimedAttestationCurrentInState(sameAttestation, sameActor, state))
                throw new UnauthorizedAccessException("The original Home READ was revoked, completed or changed actor/intent.");
        }
        finally { _gate.Release(); }
    }
}

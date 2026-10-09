using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.MiniComputer;

public sealed partial class JsonMiniComputerCatalogStore : ICanonicalMiniComputerCatalogLocation, ICanonicalMiniComputerCatalogIdentityFormat
{
    public string OriginalCatalogPath => _path;

    public async Task<ICanonicalMiniComputerCatalogReservation> ReserveOriginalCatalogWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new MiniComputerOriginalInvocation(this, scope, retain);
        var entered = false; OriginalReservation? reservation = null;
        try
        {
            // Capture successful acquisition even if the borrowed scope rejects its
            // postguard. No later caller may leak or recreate this SAME writer gate.
            await source.Read(() => _gate.WaitAsync(token), () => entered = true).ConfigureAwait(false);
            reservation = new(this);
            await source.CloseAsync().ConfigureAwait(false);
            return reservation;
        }
        catch (Exception cause)
        {
            source.Remember(cause);
            if (entered)
            {
                if (reservation is not null) await reservation.DisposeAsync().ConfigureAwait(false);
                else _gate.Release();
            }
            await source.CloseAsync().ConfigureAwait(false); throw;
        }
    }
    private sealed class OriginalReservation(JsonMiniComputerCatalogStore owner) : ICanonicalMiniComputerCatalogReservation
    {
        private int _closed;
        public bool IsOriginalCatalog(ICanonicalMiniComputerCatalogLocation sameCatalog) =>
            ReferenceEquals(owner, sameCatalog) && Volatile.Read(ref _closed) == 0;
        public void DemandOriginalReservation()
        { ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this); }
        public ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref _closed, 1) == 0) owner._gate.Release(); return ValueTask.CompletedTask; }
    }

    internal async Task<OriginalRead> ReadOriginalWithinSourceAsync(
        ICanonicalMiniComputerProtectedCatalogRead originalLease,
        MiniComputerOriginalInvocation source, CancellationToken token)
    {
        if (!originalLease.IsOriginalCatalog(this))
            throw new UnauthorizedAccessException("The protected read belongs to another actual Mini Computer catalogue.");
        var bytes = await source.Read(() => originalLease.ReadOriginalBytesWithinSourceAsync(
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var read = source.Invoke(() =>
        {
            var snapshot = ParseOriginalCatalog(bytes);
            return new OriginalRead(snapshot, Convert.ToHexString(SHA256.HashData(bytes.Span)));
        });
        await source.Read(() => originalLease.RevalidateWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
        return read;
    }
    public void ValidateOriginalIdentitySetupBytes(ReadOnlyMemory<byte> originalBytes) => _ = ParseOriginalCatalog(originalBytes);

    private static MiniComputerCatalogSnapshot ParseOriginalCatalog(ReadOnlyMemory<byte> bytes)
    {
        var snapshot = JsonSerializer.Deserialize<MiniComputerCatalogSnapshot>(bytes.Span, JsonOptions)
            ?? throw new InvalidDataException("The original Mini Computer catalogue is empty or invalid.");
        if (snapshot.SchemaVersion != CurrentSchemaVersion || snapshot.VirtualMachines.IsDefault ||
            snapshot.Snapshots.IsDefault || snapshot.Disks.IsDefault || snapshot.Media.IsDefault)
            throw new InvalidDataException("The original Mini Computer catalogue schema is unsupported or incomplete.");
        if (snapshot.VirtualMachines.Any(vm => vm is null || vm.VMID.Value == Guid.Empty ||
                vm.ProviderID.Value == Guid.Empty || vm.Revision < 1 || vm.ConfigurationVersion < 1 ||
                string.IsNullOrWhiteSpace(vm.ProviderMachineID)) ||
            snapshot.VirtualMachines.Select(vm => vm.VMID).Distinct().Count() != snapshot.VirtualMachines.Length)
            throw new InvalidDataException("The canonical Mini Computer catalogue has ambiguous VM identity or revision metadata.");
        return snapshot;
    }
    internal sealed record OriginalRead(MiniComputerCatalogSnapshot Snapshot, string Sha256);
}

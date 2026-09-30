using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NineToOne.Accounts;

internal sealed record StateEnvelope<T>(int SchemaVersion, T State);
internal static class DurableState
{
    private static readonly JsonSerializerOptions Options = AccountContractJson.CreateOptions();
    public static T Read<T>(string path)
    {
        var envelope = JsonSerializer.Deserialize<StateEnvelope<T>>(File.ReadAllText(path),Options)
            ?? throw new InvalidDataException("invalid_state");
        if (envelope.SchemaVersion != 1) throw new InvalidDataException("unsupported_state_schema");
        return envelope.State is null ? throw new InvalidDataException("invalid_state") : envelope.State;
    }
    public static void Write<T>(string path, T state)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var fileOptions=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())fileOptions.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, fileOptions))
            { JsonSerializer.Serialize(stream, new StateEnvelope<T>(1, state),Options); stream.Flush(true); }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // Named OS mutex coordinates instances and processes on this host. Multi-host deployments require a transactional database.
    public static IDisposable Acquire(string path) => new Lease(path);
    private sealed class Lease : IDisposable
    {
        private readonly Mutex mutex;
        public Lease(string path)
        {
            var canonical = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
            var name = "9to1-state-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            mutex = new Mutex(false, name);
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(30))) throw new TimeoutException("state_busy"); }
            catch (AbandonedMutexException) { /* Atomic committed file is the recovery point. */ }
            catch { mutex.Dispose(); throw; }
        }
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}

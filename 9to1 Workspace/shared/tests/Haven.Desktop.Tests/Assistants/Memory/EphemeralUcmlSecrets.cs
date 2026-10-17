using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Memory.Tests;

// Partial of AssistantOriginalMemoryTests. The rig (AssistantOriginalMemoryTests.Rig.cs)
// references EphemeralUcmlSecrets, which is defined in AssistantUcmlEncryptionTests.cs in
// the Assistants.Memory.Tests project but is not compiled into Haven.Desktop.Tests.
// This partial supplies the same shape so the Haven.Desktop.Tests build is unblocked without
// editing UCML-owned files. On Linux the rig uses LinuxProviderSecretStore, so this
// implementation is never exercised; it exists only to satisfy compilation.
public sealed partial class AssistantOriginalMemoryTests
{
    private sealed class EphemeralUcmlSecrets : IProviderSecretStore
    {
        private readonly Dictionary<(string, string), string> _values = [];
        public Task SetAsync(string provider, string name, string value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values[(provider, name)] = value; return Task.CompletedTask; }
        public Task<string?> GetAsync(string provider, string name, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_values.GetValueOrDefault((provider, name))); }
        public Task DeleteAsync(string provider, string name, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values.Remove((provider, name)); return Task.CompletedTask; }
    }
}

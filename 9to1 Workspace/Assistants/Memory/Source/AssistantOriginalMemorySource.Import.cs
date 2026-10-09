using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource : IHomeOriginalScopedResourceStoreIdentitySource
{
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly object _importGate = new();
    private HomeOriginalLocalStoreImportSession? _memoryImports;

    public void BindOriginalMemoryImportSession(HomeOriginalLocalStoreImportSession sameSession,
        CanonicalSqliteOriginalStoreEvidenceProvider sameEvidence)
    {
        ArgumentNullException.ThrowIfNull(sameSession); ArgumentNullException.ThrowIfNull(sameEvidence);
        lock (_importGate)
        {
            if (OriginalClose is not null || sameSession.OriginalClose is not null ||
                _memoryImports is not null && !ReferenceEquals(_memoryImports, sameSession) ||
                !sameEvidence.HasOriginalComposition(_store, ResourceKind) ||
                !sameSession.IsOriginalSource(this, sameEvidence, _profiles, _ownership))
                throw new InvalidOperationException("Bind the SAME live memory source, configured evidence and Home import session once before presentation.");
            _memoryImports = sameSession;
        }
    }
    public bool HasOriginalMemoryImportSession(HomeOriginalLocalStoreImportSession sameSession)
    { lock (_importGate) return ReferenceEquals(_memoryImports, sameSession); }
    internal HomeOriginalLocalStoreImportSession? OriginalMemoryImportSession
    { get { lock (_importGate) return _memoryImports; } }

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) =>
        GetStoreIdentityWithinOriginalSourceAsync(body => body(), _ => { }, token);
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) => new(RunAsync(scope, retain, async source =>
    {
        var actor = await source.Read(() => _profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Recover the current Home profile before inspecting the configured memory store.");
        return await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor,
            source.Run, source.Retain, token)).ConfigureAwait(false);
    }));

    internal Task<string?> ValidateOriginalMemoryImportBindingAsync(AssistantConversationBinding binding,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunAsync<string?>(scope, retain, async source =>
    {
        var reason = ConfigurationRefusal(binding.Definition);
        if (reason is not null) return reason; // Disabled memory performs no source query.
        var membership = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        if (membership is null || !ReferenceEquals(membership.OriginalHomeDenFactory, _home) ||
            !ReferenceEquals(membership.OriginalConversations, _conversations))
            return "Open this Assistant through its actual current conversation owner before reviewing memory import.";
        var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        return ConfigurationRefusal(current.Definition);
    });
}

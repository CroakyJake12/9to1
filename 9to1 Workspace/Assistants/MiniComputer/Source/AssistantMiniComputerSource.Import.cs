using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.MiniComputer;

public sealed partial class AssistantMiniComputerSource
{
    private HomeOriginalLocalStoreImportSession? _catalogImports;
    public void BindOriginalMiniComputerImportSession(HomeOriginalLocalStoreImportSession sameSession)
    {
        ArgumentNullException.ThrowIfNull(sameSession);
        lock (_gate)
        {
            if (OriginalClose is not null || sameSession.OriginalClose is not null ||
                _catalogImports is not null && !ReferenceEquals(_catalogImports, sameSession) ||
                !sameSession.IsOriginalSource(_catalog, _catalog, _profiles, _ownership))
                throw new InvalidOperationException("Bind the SAME live protected catalogue, evidence, Home profile and import session before presentation.");
            _catalogImports = sameSession;
        }
    }
    public bool HasOriginalMiniComputerImportSession(HomeOriginalLocalStoreImportSession sameSession)
    { lock (_gate) return ReferenceEquals(_catalogImports, sameSession); }
    internal HomeOriginalLocalStoreImportSession? OriginalImportSession
    { get { lock (_gate) return _catalogImports; } }
    internal Task<string?> ValidateOriginalImportBindingAsync(AssistantConversationBinding binding,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Run<string?>(scope, retain, async source =>
    {
        var membership = RequireMembership(binding);
        var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!current.Definition.Configuration.Enabled || current.Definition.Configuration.Archived)
            return "Enable this current Assistant before reviewing its Mini Computer catalogue.";
        var lease = await source.Read(() => _catalog.AcquireOriginalProtectedReadWithinSourceAsync(current.Actor,
            source.Run, source.Retain, token), value => { if (value is not null) source.OwnAsync(value); }).ConfigureAwait(false);
        if (lease is null) return "The configured Mini Computer catalogue needs explicit owning setup before Home import. Opening this view does not create an identity or VM.";
        var final = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (final.Actor != current.Actor) throw new UnauthorizedAccessException("The current Home actor changed while inspecting catalogue identity.");
        return null;
    });
}

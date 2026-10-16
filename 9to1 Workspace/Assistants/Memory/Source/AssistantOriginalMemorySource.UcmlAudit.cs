using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    private HomeUcmlAccessAuditOwner? _ucmlAuditOwner;
    public void BindOriginalUcmlAccessAudit(HomeUcmlAccessAuditOwner actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualOwner);
        lock (_writeGate)
        {
            if (_ucmlInputsIssued || _ucmlAuditOwner is not null || !actualOwner.HasOriginalComposition(this, _profiles))
                throw new UnauthorizedAccessException("Bind the SAME Home UCML access audit once before issuing memory inputs.");
            _ucmlAuditOwner = actualOwner;
        }
    }
    internal Task RecordOriginalUcmlAccessWithinSourceAsync(IChatOriginalPersistentMemoryInput? input,
        Guid requestId, HomeUcmlAccessOutcome outcome, IReadOnlyList<Guid> records,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunAsync(scope, retain, async source =>
        {
            var original = input is null ? null : DemandInput(input);
            var owner = _ucmlAuditOwner ?? throw new UnauthorizedAccessException("UCML access requires the original durable Home audit owner.");
            await source.Read(() => owner.RecordWithinOriginalSourceAsync(this, input, original?.Actor,
                requestId, outcome, records, source.Run, source.Retain, token)).ConfigureAwait(false);
            return true;
        });
}

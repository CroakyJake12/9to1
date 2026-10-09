using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>The neutral port is this SAME protected owner. Wrappers retain the
/// actual source-issued page/window; they introduce no SQL, store or permission path.</summary>
public sealed partial class CanonicalAutomationLibraryOriginalReadOwner : ICanonicalAutomationLibraryOriginalReadSource
{
    private sealed class PortContinuation(CanonicalAutomationLibraryOriginalReadOwner owner,
        IOriginalLibraryContinuation original) : ICanonicalAutomationLibraryOriginalContinuation
    {
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Owner = owner;
        internal readonly IOriginalLibraryContinuation Original = original;
    }
    private sealed class PortObservation : ICanonicalAutomationLibraryOriginalObservation
    {
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Owner;
        internal readonly IOriginalLibraryObservation Original;
        internal PortObservation(CanonicalAutomationLibraryOriginalReadOwner owner, IOriginalLibraryObservation original)
        {
            Owner = owner; Original = original;
            NextContinuation = original.NextContinuation is { } cursor ? new PortContinuation(owner, cursor) : null;
        }
        public AuthenticatedResourceActor Actor => Original.Actor;
        public ResourceStoreIdentity? OriginalStoreIdentity => Original.OriginalStoreIdentity;
        public CanonicalAutomationLibraryReadState State => Original.State switch
        {
            LibraryState.Available => CanonicalAutomationLibraryReadState.Available,
            LibraryState.SetupRequired => CanonicalAutomationLibraryReadState.SetupRequired,
            _ => throw new InvalidDataException("The actual library observation has an unsupported state.")
        };
        public string Detail => Original.Detail;
        public IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions => Original.Definitions;
        public ICanonicalAutomationLibraryOriginalContinuation? NextContinuation { get; }
    }
    private bool IsIssuedPortObservation(ICanonicalAutomationLibraryOriginalObservation actual) =>
        actual is PortObservation observed && ReferenceEquals(observed.Owner, this) &&
        IsIssuedOriginalObservation(observed.Original);
    private bool IsIssuedPortContinuation(ICanonicalAutomationLibraryOriginalContinuation actual) =>
        actual is PortContinuation cursor && ReferenceEquals(cursor.Owner, this) &&
        IsIssuedOriginalContinuation(cursor.Original);

    bool ICanonicalAutomationLibraryOriginalReadSource.IsIssuedOriginalObservation(
        ICanonicalAutomationLibraryOriginalObservation actual) => IsIssuedPortObservation(actual);
    bool ICanonicalAutomationLibraryOriginalReadSource.IsIssuedOriginalContinuation(
        ICanonicalAutomationLibraryOriginalContinuation actual) => IsIssuedPortContinuation(actual);

    Task<ICanonicalAutomationLibraryOriginalObservation> ICanonicalAutomationLibraryOriginalReadSource.ReadOriginalLibraryWithinSourceAsync(
        AuthenticatedResourceActor actualActor, AutomationLibraryQuery query,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        ICanonicalAutomationLibraryOriginalContinuation? continuation)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader<ICanonicalAutomationLibraryOriginalObservation>(source, async () =>
        {
            var window = source.Invoke(() => continuation is null ? null : IsIssuedPortContinuation(continuation)
                ? ((PortContinuation)continuation).Original
                : throw new UnauthorizedAccessException("Use the SAME original library continuation."));
            await source.JoinAllAsync().ConfigureAwait(false);
            var original = await source.Read(() => ReadOriginalLibraryWithinSourceAsync(actualActor, query,
                source.Run, source.Retain, token, window)).ConfigureAwait(false);
            var result = source.Invoke(() => IsIssuedOriginalObservation(original) && original.Actor == actualActor
                ? new PortObservation(this, original)
                : throw new UnauthorizedAccessException("The actual protected library did not issue this page."));
            await source.JoinAllAsync().ConfigureAwait(false); return result;
        });
    }
    Task ICanonicalAutomationLibraryOriginalReadSource.RevalidateOriginalObservationWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameActual, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return _store.RetainOriginalReader(source, async () =>
        {
            var original = source.Invoke(() => IsIssuedPortObservation(sameActual) && sameActual.Actor == actualActor
                ? ((PortObservation)sameActual).Original
                : throw new UnauthorizedAccessException("Use the SAME source-issued library page/current actor."));
            await source.JoinAllAsync().ConfigureAwait(false);
            await source.Read(() => RevalidateOriginalObservationWithinSourceAsync(original, actualActor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        });
    }
}

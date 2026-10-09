namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private IChatOriginalPersistentMemorySource? OriginalPersistentMemorySource =>
        Volatile.Read(ref _originalPersistentMemorySource);

    /// <summary>Publishes the actual optional source after its lazy Home/Den composition.
    /// This one-time object association issues no input or resource authorization.</summary>
    public void BindOriginalPersistentMemorySource(IChatOriginalPersistentMemorySource actualSource)
    {
        ArgumentNullException.ThrowIfNull(actualSource);
        var existing = Interlocked.CompareExchange(ref _originalPersistentMemorySource, actualSource, null);
        if (existing is not null && !ReferenceEquals(existing, actualSource))
            throw new InvalidOperationException("The actual Chat persistent-memory source cannot be replaced.");
    }
}

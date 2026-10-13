namespace Haven.Application;

/// <summary>A transient user's choice for one temporary send. This additional restriction
/// never grants access to a memory source, account, resource or provider.</summary>
public sealed class TemporaryChatMemoryReadChoice
{
    internal TemporaryChatMemoryReadChoice(bool allowRead) => AllowRead = allowRead;
    public bool AllowRead { get; }
}

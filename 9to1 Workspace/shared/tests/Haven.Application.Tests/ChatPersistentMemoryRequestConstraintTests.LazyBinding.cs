using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Lazy_source_binding_keeps_general_Chat_construction_independent_and_then_uses_actual_source()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy);
        Assert.False(rig.OriginalChat.HasOriginalPersistentMemorySource(scoped));
        Assert.Equal(0, legacy.Reads); Assert.Equal(0, scoped.Reads + scoped.Validations);
        var refused = await Record.ExceptionAsync(() => rig.SendOwnedAsync(ScopedOptions(scoped)));
        Assert.True(HasMessage(refused, "Persistent-memory opt-in requires the SAME live scoped source and original Chat producer."));
        Assert.Empty(rig.Provider.Requests); Assert.Equal(0, legacy.Reads + scoped.Reads);
        rig.OriginalChat.BindOriginalPersistentMemorySource(scoped);
        rig.OriginalChat.BindOriginalPersistentMemorySource(scoped);
        Assert.True(rig.OriginalChat.HasOriginalPersistentMemorySource(scoped));
        await rig.SendOwnedAsync(ScopedOptions(scoped));
        Assert.Equal(1, scoped.Reads); Assert.Equal(0, legacy.Reads);
        Assert.Contains(ScopedMemorySource.Sentinel, Assert.Single(rig.Provider.Requests).SystemPrompt ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constructor_source_cannot_be_replaced_by_lazy_publication_or_foreign_marker()
    {
        var legacy = new MemorySource(); var actual = new ScopedMemorySource(); var foreign = new ScopedMemorySource();
        var rig = new Rig(legacy, actual);
        rig.OriginalChat.BindOriginalPersistentMemorySource(actual);
        Assert.Throws<InvalidOperationException>(() => rig.OriginalChat.BindOriginalPersistentMemorySource(foreign));
        Assert.True(rig.OriginalChat.HasOriginalPersistentMemorySource(actual));
        Assert.False(rig.OriginalChat.HasOriginalPersistentMemorySource(foreign));
        var refused = await Record.ExceptionAsync(() => rig.SendOwnedAsync(ScopedOptions(foreign)));
        Assert.True(HasMessage(refused, "The scoped memory input was not issued by this SAME configured source."));
        Assert.Empty(rig.Provider.Requests); Assert.Equal(0, foreign.Reads + actual.Reads + legacy.Reads);
        await rig.SendOwnedAsync(ScopedOptions(actual));
        Assert.Equal(1, actual.Reads); Assert.Equal(0, foreign.Reads + legacy.Reads);
        Assert.Single(rig.Provider.Requests);
    }
}

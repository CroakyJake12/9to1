using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-GAMES-003: input bindings identify device/action/context, support remapping, and separate shared defaults from personal overrides.
/// Proves remap identity retention, collision rejection, stale-edit no-mutation, and that a personal override never mutates the shared default.</summary>
public sealed class GamesInputBindingsTests
{
    private static readonly Guid Jump = Guid.NewGuid();
    private static readonly Guid Fire = Guid.NewGuid();

    private static GamesInputBinding KeyboardJump(Guid id, string key = "Space") => new(id, GamesInputDevice.Keyboard, "Jump", "Gameplay", key);
    private static GamesInputBinding GamepadJump(string key = "A") => new(Guid.NewGuid(), GamesInputDevice.Gamepad, "Jump", "Gameplay", key);

    private static GamesInputMap MakeMap() => GamesInputMap.Create([KeyboardJump(Jump), new GamesInputBinding(Fire, GamesInputDevice.Keyboard, "Fire", "Gameplay", "LeftMouse")]);

    [Fact]
    public void CreateAssignsStableIdentitiesAndRejectsCollisions()
    {
        var map = MakeMap();
        Assert.Equal(1, map.Revision);
        Assert.Equal(2, map.Bindings.Count);
        // Two bindings sharing device+action+context collide.
        Assert.Throws<InvalidOperationException>(() => GamesInputMap.Create([KeyboardJump(Jump), KeyboardJump(Guid.NewGuid(), "K")]));
    }

    [Fact]
    public void RemapPreservesIdentityAndChangesOnlyPhysicalKey()
    {
        var map = MakeMap();
        var remapped = map.Remap(Jump, "W", 1);
        Assert.Equal(2, remapped.Revision);
        var binding = remapped.Bindings.Single(b => b.BindingID == Jump);
        // Identity (device/action/context) retained; only the physical key changed.
        Assert.Equal(GamesInputDevice.Keyboard, binding.Device);
        Assert.Equal("Jump", binding.Action);
        Assert.Equal("Gameplay", binding.Context);
        Assert.Equal("W", binding.PhysicalKey);
        // Original map is immutable.
        Assert.Equal("Space", map.Bindings.Single(b => b.BindingID == Jump).PhysicalKey);
    }

    [Fact]
    public void RemapRejectsCollisionAndStaleRevision()
    {
        var map = MakeMap();
        // Remapping Fire onto the same device/action/context as another binding is a collision.
        Assert.Throws<InvalidOperationException>(() => map.Remap(Fire, "Space", 1));
        Assert.Throws<InvalidOperationException>(() => map.Remap(Jump, "W", 99));
    }

    [Fact]
    public void AddRejectsDuplicateIdentityAndStaleRevision()
    {
        var map = MakeMap();
        Assert.Throws<InvalidOperationException>(() => map.Add(KeyboardJump(Jump), 1));
        Assert.Throws<InvalidOperationException>(() => map.Add(GamepadJump(), 99));
    }

    [Fact]
    public void PersonalOverrideResolvesIndependentlyAndNeverMutatesSharedDefault()
    {
        var shared = MakeMap();
        var jump = shared.Bindings.Single(b => b.BindingID == Jump);
        var player = new GamesInputOverride(Guid.NewGuid(), 1, null);

        // Player overrides the physical key for their own Jump binding.
        var overridden = player.SetOverride(Jump, "W", 1);
        Assert.Equal("W", overridden.Resolve(jump));
        // The shared default is unchanged and other players still resolve to it.
        Assert.Equal("Space", jump.PhysicalKey);
        Assert.Equal("Space", new GamesInputOverride(Guid.NewGuid(), 1, null).Resolve(jump));

        // Clearing the override restores the inherited shared value.
        var cleared = overridden.ClearOverride(Jump, 2);
        Assert.Equal("Space", cleared.Resolve(jump));
    }

    [Fact]
    public void OverrideStaleRevisionIsRejectedWithoutMutation()
    {
        var player = new GamesInputOverride(Guid.NewGuid(), 1, null);
        Assert.Throws<InvalidOperationException>(() => player.SetOverride(Jump, "W", 99));
        Assert.Equal(1, player.Revision);
        Assert.Empty(player.PhysicalKeyOverrides);
    }

    [Fact]
    public void DeviceDifferentiationAllowsSameActionPerDevice()
    {
        // The same action in the same context is legal across different devices.
        var map = GamesInputMap.Create([KeyboardJump(Jump), GamepadJump()]);
        Assert.Equal(2, map.Bindings.Count);
        Assert.Contains(map.Bindings, b => b.Device == GamesInputDevice.Keyboard);
        Assert.Contains(map.Bindings, b => b.Device == GamesInputDevice.Gamepad);
    }

    [Fact]
    public void InvalidTokensAndEmptyKeyAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new GamesInputBinding(Guid.NewGuid(), GamesInputDevice.Keyboard, "", "Gameplay", "Space").Capture());
        Assert.Throws<InvalidDataException>(() => new GamesInputBinding(Guid.NewGuid(), (GamesInputDevice)99, "Jump", "Gameplay", "Space").Capture());
        Assert.Throws<InvalidDataException>(() => new GamesInputBinding(Guid.Empty, GamesInputDevice.Keyboard, "Jump", "Gameplay", "Space").Capture());
    }
}

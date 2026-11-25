namespace Haven.Core.Games;

/// <summary>The physical/logical device family a binding is authored for. A binding identifies its device so the same action can map differently per device.</summary>
public enum GamesInputDevice { Keyboard, Mouse, Gamepad, Touch }

/// <summary>A single declarative input binding: which device fires it, the logical gameplay action, and the context in which it is active.
/// The identity of a binding is (Device, Action, Context); remapping changes the physical key/button while preserving this identity.</summary>
public sealed record GamesInputBinding(Guid BindingID, GamesInputDevice Device, string Action, string Context, string PhysicalKey)
{
    public const int MaximumBindings = 512;

    public GamesInputBinding Capture()
    {
        if (BindingID == Guid.Empty) throw new InvalidDataException("Games input binding identity is missing.");
        if (!Enum.IsDefined(Device)) throw new InvalidDataException("Games input binding device is unsupported.");
        if (!IsValidToken(Action)) throw new InvalidDataException("Games input binding action must be a non-empty identifier.");
        if (!IsValidToken(Context)) throw new InvalidDataException("Games input binding context must be a non-empty identifier.");
        if (string.IsNullOrWhiteSpace(PhysicalKey)) throw new InvalidDataException("Games input binding physical key is required.");
        return this;
    }

    /// <summary>Two bindings collide when they share device, action and context: one logical action cannot have two sources in the same context.</summary>
    public bool CollidesWith(GamesInputBinding other) => other is not null && Device == other.Device
        && string.Equals(Action, other.Action, StringComparison.Ordinal)
        && string.Equals(Context, other.Context, StringComparison.Ordinal);

    private static bool IsValidToken(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
}

/// <summary>The shared authored default input map. This is the project-wide baseline every player inherits unless they carry a personal override.
/// It is revision-aware so remap transactions are compare/exchange and never mutate the original.</summary>
public sealed record GamesInputMap(int Revision, IReadOnlyList<GamesInputBinding> Bindings)
{
    public const int CurrentSchemaVersion = 1;

    public GamesInputMap Capture()
    {
        if (Revision < 1) throw new InvalidDataException("Games input map revision must be positive.");
        if (Bindings is null) throw new InvalidDataException("Games input map bindings are missing.");
        var snapshot = Bindings.Select(binding => (binding?.Capture() ?? throw new InvalidDataException("Games input map contains a missing binding."))).ToArray();
        if (snapshot.Length > GamesInputBinding.MaximumBindings) throw new InvalidDataException("Games input map exceeds the configured binding limit.");
        if (snapshot.Select(b => b.BindingID).Distinct().Count() != snapshot.Length) throw new InvalidDataException("Games input map contains duplicate binding identities.");
        for (var i = 0; i < snapshot.Length; i++)
            for (var j = i + 1; j < snapshot.Length; j++)
                if (snapshot[i].CollidesWith(snapshot[j])) throw new InvalidOperationException("GamesInputBindingCollision");
        return this with { Bindings = snapshot };
    }

    public static GamesInputMap Create(IEnumerable<GamesInputBinding> bindings) => new GamesInputMap(1, bindings.ToArray()).Capture();

    private GamesInputMap Next(int expectedRevision, IReadOnlyList<GamesInputBinding> bindings)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesInputRevisionConflict");
        return new GamesInputMap(checked(Revision + 1), bindings).Capture();
    }

    public GamesInputMap Add(GamesInputBinding binding, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (Bindings.Any(existing => existing.BindingID == binding.BindingID)) throw new InvalidOperationException("GamesInputBindingAlreadyPresent");
        return Next(expectedRevision, Bindings.Append(binding).ToArray());
    }

    /// <summary>Remap preserves the binding identity (device/action/context) and only changes the physical key. A collision with another binding is rejected.</summary>
    public GamesInputMap Remap(Guid bindingID, string physicalKey, int expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(physicalKey)) throw new ArgumentException("A remap requires a physical key.", nameof(physicalKey));
        var found = Bindings.SingleOrDefault(binding => binding.BindingID == bindingID) ?? throw new KeyNotFoundException("GamesInputBindingNotFound");
        var remapped = found with { PhysicalKey = physicalKey };
        if (Bindings.Any(other => other.BindingID != bindingID && other.CollidesWith(remapped))) throw new InvalidOperationException("GamesInputBindingCollision");
        return Next(expectedRevision, Bindings.Select(binding => binding.BindingID == bindingID ? remapped : binding).ToArray());
    }

    public GamesInputMap Remove(Guid bindingID, int expectedRevision)
    {
        if (!Bindings.Any(binding => binding.BindingID == bindingID)) throw new KeyNotFoundException("GamesInputBindingNotFound");
        return Next(expectedRevision, Bindings.Where(binding => binding.BindingID != bindingID).ToArray());
    }
}

/// <summary>A personal override layered on top of the shared default map. It references bindings by identity and only stores the player's own physical-key changes.
/// Applying an override never mutates the shared default; removing an override restores the inherited value.</summary>
public sealed record GamesInputOverride
{
    public const int CurrentSchemaVersion = 1;

    [System.Text.Json.Serialization.JsonConstructor]
    public GamesInputOverride(Guid playerID, int revision, IReadOnlyDictionary<Guid, string>? physicalKeyOverrides)
    {
        if (playerID == Guid.Empty) throw new InvalidDataException("Games input override player identity is missing.");
        if (revision < 1) throw new InvalidDataException("Games input override revision must be positive.");
        PlayerID = playerID;
        Revision = revision;
        PhysicalKeyOverrides = physicalKeyOverrides is null ? new Dictionary<Guid, string>() : new Dictionary<Guid, string>(physicalKeyOverrides);
    }

    public Guid PlayerID { get; }
    public int Revision { get; }
    public IReadOnlyDictionary<Guid, string> PhysicalKeyOverrides { get; }

    /// <summary>Resolve the effective physical key for a binding: the personal override when present, otherwise the shared default. The shared map is read-only here.</summary>
    public string Resolve(GamesInputBinding sharedDefault) =>
        sharedDefault is null ? throw new ArgumentNullException(nameof(sharedDefault))
        : PhysicalKeyOverrides.TryGetValue(sharedDefault.BindingID, out var key) && !string.IsNullOrWhiteSpace(key) ? key : sharedDefault.PhysicalKey;

    /// <summary>Set or replace this player's override for a binding. The shared default map is never touched.</summary>
    public GamesInputOverride SetOverride(Guid bindingID, string physicalKey, int expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(physicalKey)) throw new ArgumentException("An override requires a physical key.", nameof(physicalKey));
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesInputRevisionConflict");
        var next = new Dictionary<Guid, string>(PhysicalKeyOverrides) { [bindingID] = physicalKey };
        return new GamesInputOverride(PlayerID, checked(Revision + 1), next);
    }

    /// <summary>Clear this player's override so the binding falls back to the shared default.</summary>
    public GamesInputOverride ClearOverride(Guid bindingID, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesInputRevisionConflict");
        var next = new Dictionary<Guid, string>(PhysicalKeyOverrides);
        next.Remove(bindingID);
        return new GamesInputOverride(PlayerID, checked(Revision + 1), next);
    }
}

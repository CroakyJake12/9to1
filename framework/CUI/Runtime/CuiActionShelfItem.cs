namespace CakeOS.Cui.Runtime;

/// <summary>One eligible command identity. This is data, never an activation grant or a
/// visual tree. The host must freshly authorize the original command when dispatched.</summary>
public sealed record CuiActionShelfItem(string Id, string Command, string Label,
    string IconSource, string Tooltip, object? Parameter = null, object? ContextIdentity = null);

/// <summary>Privately captured by the owning shelf before the loader's async admission gate.</summary>
internal sealed record CuiActionShelfActivation(CuiActionShelf Owner, int Generation, CuiActionShelfItem Item);

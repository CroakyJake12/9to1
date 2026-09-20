namespace CakeOS.Platform;

/// <summary>Platform-neutral root element interface. UI frameworks provide implementations.</summary>
public interface IRootElement
{
}

/// <summary>
/// A platform-neutral root that exposes the framework-owned element a compatible
/// host can mount. Hosts must reject roots for frameworks they do not support.
/// </summary>
public interface IHuiRootElement : IRootElement
{
    object NativeRoot { get; }
}

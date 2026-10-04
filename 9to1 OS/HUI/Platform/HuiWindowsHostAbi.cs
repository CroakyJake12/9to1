namespace CakeOS.Platform;

/// <summary>Versioned ABI advertised by a CakeUI Windows root provider. Reuses the shared
/// <see cref="HuiRootProviderAbi"/> shape; only the expected contract id differs from Linux.</summary>
public static class HuiWindowsHostAbi
{
    public const string ContractId = "cakeos.hui.windows-root-provider";
    public const int CurrentVersion = 1;

    public static HuiRootProviderAbi Current { get; } = new(ContractId, CurrentVersion);

    public static void RequireCompatible(HuiRootProviderAbi abi)
    {
        ArgumentNullException.ThrowIfNull(abi);
        if (!string.Equals(abi.ContractId, ContractId, StringComparison.Ordinal))
            throw new NotSupportedException($"Unsupported HUI root-provider ABI '{abi.ContractId}'.");
        if (abi.Version != CurrentVersion)
            throw new NotSupportedException($"HUI root-provider ABI version {abi.Version} is unsupported; host requires version {CurrentVersion}.");
    }
}

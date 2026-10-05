namespace Haven.Application.Compatibility;

/// <summary>
/// Navigation into the OS package manager with retained Files-owner metadata.
/// Selection data is not permission, signer trust, installation approval or an execution handle.
/// The host must reread the canonical Files lease and original current actor before presenting content.
/// Final installation uses the owning Home permission/execution boundary separately.
/// </summary>
public interface ICompatibilityPackageOpenHandler
{
    Task OpenAsync(CompatibilityPackageSource selection, CancellationToken cancellationToken);
}

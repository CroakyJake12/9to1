using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Home.Core;
namespace HavenOS.Files.NativeHost;

/// <summary>HOME-process stages only. Registration never transfers raw Home owners through app IPC.</summary>
public sealed class FilesNativeHomeDomainRegistration
{
    private readonly object _sync = new();
    private HomeNativeWindowsIdentityComponents? _identity;
    private HomeNativeWindowsOwnershipComponents? _ownership;
    private HomeNativeWindowsOwnerComponents? _owners;
    private NativeFilesWorkspaceService? _workspace;
    private NativeFilesWorkspaceAuthority? _authority;
    private FilesNativeBrowserService? _browser;
    private FilesCompatibilityPackageContentSource? _packages;
    private FilesArtifactResourceResolver? _items;
    private FilesNativeStoreReadResolver? _stores;
    private bool _factoryIssued;
    private bool _factoryInvoked;
    private readonly IFilesNativeOriginalPublicationSource? _originalPublicationSource;

    public FilesNativeHomeDomainRegistration(IFilesNativeOriginalPublicationSource? originalPublicationSource = null)
    { _originalPublicationSource = originalPublicationSource; }

    public HomeNativeWindowsStoreRegistrations ConfigureOriginalStores(HomeNativeWindowsIdentityComponents identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_sync)
        {
            if (_identity is not null) throw new InvalidOperationException("Retain the original Files evidence stage once.");
            _identity = identity;
            _workspace = new(identity.StateStore, identity.Profiles);
            return new(Array.AsReadOnly<IHomeLocalStoreEvidenceProvider>([_workspace]),
                new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>()));
        }
    }
    public HomeNativeWindowsResolverRegistrations ConfigureOriginalResolvers(HomeNativeWindowsOwnershipComponents ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        lock (_sync)
        {
            if (_workspace is null || !ReferenceEquals(ownership.Identity, _identity) || _ownership is not null ||
                !_workspace.IsBoundToOriginalLocalComposition(ownership.Identity.Profiles, ownership.Ownership))
                throw new UnauthorizedAccessException("Retain the SAME completed Home evidence and ownership stage.");
            _ownership = ownership;
            _authority = new(_workspace, ownership.Identity.Profiles, ownership.Ownership);
            _items = new(_authority);
            _stores = new(_authority, ownership.Identity.Profiles);
            return new(Array.AsReadOnly<ICanonicalResourceAccessResolver>([_items, _stores]),
                new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>()));
        }
    }
    public IReadOnlyDictionary<Type, object> ConfigureOriginalOwners(HomeNativeWindowsOwnerComponents owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        lock (_sync)
        {
            if (_identity is null || _ownership is null || _authority is null || _owners is not null ||
                !ReferenceEquals(owners.StateStore, _identity.StateStore) || !ReferenceEquals(owners.Profiles, _identity.Profiles) ||
                !ReferenceEquals(owners.Permissions, _identity.Permissions) || !ReferenceEquals(owners.Ownership, _ownership.Ownership) ||
                !owners.Resources.IsBoundToActorSource(owners.Profiles) ||
                !owners.Broker.IsBoundToOriginalComposition(owners.Resources, owners.Permissions))
                throw new UnauthorizedAccessException("Use the SAME canonical completed Home owner stage.");
            _owners = owners;
            _packages = new(_authority, owners.Profiles, owners.Resources);
            _browser = new(_authority, owners.Profiles, owners.Resources, _packages);
            return new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>
            {
                [typeof(NativeFilesWorkspaceService)] = _workspace!,
                [typeof(NativeFilesWorkspaceAuthority)] = _authority,
                [typeof(FilesArtifactResourceResolver)] = _items!,
                [typeof(FilesNativeStoreReadResolver)] = _stores!,
                [typeof(FilesCompatibilityPackageContentSource)] = _packages,
                [typeof(ICompatibilityPackageContentSource)] = _packages,
                [typeof(FilesNativeBrowserService)] = _browser,
                [typeof(IResourceStoreOwnershipAuthority)] = owners.Ownership
            });
        }
    }
    public Func<HomeNativeCoreApiSessions, IHomeNativeFilesDomainOwner?> ConfigureOriginalFilesOwner(
        HomeNativeWindowsOwnerComponents owners)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(owners, _owners) || _browser is null || _authority is null ||
                _packages is null || _factoryIssued ||
                !_browser.IsBoundToOriginalComposition(_authority, owners.Profiles, owners.Resources, _packages))
                throw new UnauthorizedAccessException("Complete this SAME original Files Home registration before private issuance.");
            _factoryIssued = true;
            return issuer =>
            {
                lock (_sync)
                {
                    if (_factoryInvoked) throw new InvalidOperationException("The original Files owner factory is already consumed.");
                    _factoryInvoked = true;
                    return new FilesNativeHomeDomainOwner(issuer, _authority, owners.Profiles, _browser,
                        owners.Resources, owners.Broker, owners.Permissions, _originalPublicationSource);
                }
            };
        }
    }
}

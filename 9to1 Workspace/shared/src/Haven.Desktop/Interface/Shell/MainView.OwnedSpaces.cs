using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly SemaphoreSlim _spacesWorkspaceGate = new(1, 1);
    private OwnedSpacesWorkspace? _ownedSpacesWorkspace;

    private async Task<OwnedSpacesWorkspace> GetOwnedSpacesWorkspaceAsync(CancellationToken token)
    {
        await _spacesWorkspaceGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_ownedSpacesWorkspace is null)
            {
                var services = App.Services ?? throw new InvalidOperationException("Home services are unavailable.");
                var receipts = services.GetRequiredService<IResourceStoreOwnershipAuthority>() as IResourceStoreOwnershipReceiptAuthority
                    ?? throw new UnauthorizedAccessException("Home ownership receipts are unavailable.");
                _ownedSpacesWorkspace = await OwnedSpacesWorkspace.OpenAsync(_versionedSettings as VersionedAtomicSettingsStore
                    ?? throw new InvalidOperationException("The canonical Spaces settings store is unavailable."),
                    services.GetRequiredService<IAuthenticatedResourceActorSource>(), receipts,
                    services.GetRequiredService<SqliteDatabase>(), services.GetRequiredService<ConversationLocalStoreAuthority>(),
                    services.GetRequiredService<IConversationSpaceCommitStore>(), () => !IsDisposed, token);
            }
            await _ownedSpacesWorkspace.RequireCurrentAccessAsync(token);
            return _ownedSpacesWorkspace;
        }
        finally { _spacesWorkspaceGate.Release(); }
    }

    private async ValueTask<ISettingsCommitAdmission> CaptureSpaceWriteAdmissionAsync(CancellationToken token)
    {
        var workspace = await GetOwnedSpacesWorkspaceAsync(token);
        var services = App.Services ?? throw new InvalidOperationException("Home services are unavailable.");
        var authority = new SpaceLocalStoreAuthority(_versionedSettings as IResourceStoreIdentitySource
            ?? throw new InvalidOperationException("The canonical Spaces store identity is unavailable."),
            services.GetRequiredService<IAuthenticatedResourceActorSource>(),
            services.GetRequiredService<IResourceStoreOwnershipAuthority>(), () => !IsDisposed);
        return await authority.CaptureWriteAdmissionForActorAsync(workspace.Actor, token);
    }

    private Task<Conversation> CreateOwnedSpaceChatAsync(Conversation proposed, CancellationToken token) =>
        new OwnedSpaceChatLifecycle(GetOwnedSpacesWorkspaceAsync, _conversations)
            .CreateAsync(proposed, _nativeChatSidebar?.CurrentSpaceId, token);

    private Task<Conversation> AssignOwnedSpaceAsync(Conversation expected, Guid? destinationId, CancellationToken token) =>
        new OwnedSpaceChatLifecycle(GetOwnedSpacesWorkspaceAsync, _conversations).AssignAsync(expected, destinationId, token);
}

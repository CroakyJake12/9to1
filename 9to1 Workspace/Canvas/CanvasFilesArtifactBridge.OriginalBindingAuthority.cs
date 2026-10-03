using Haven.Application;
using HavenOS.Files;
namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasFilesArtifactBridge
{
    private CanvasFilesFinalAuthority RetainOriginalBindingFinalAuthority(OriginalDisplaySelection selected,
        CanvasFilesFinalAuthority homeAuthority)
    {
        // Caller is the strong original Save already retaining this private token's commit gate.
        var lifetime=new OriginalBindingAuthorityLifetime(this,selected,homeAuthority.ValidateOriginalAsync,homeAuthority);
        return new(new FilesCommitAuthorityGuard(selected.Actor.ActorId,lifetime.ValidateAsync),lifetime);
    }

    // hostAllowsWrites is a deny-only synchronous host lifetime callback; it must perform no Home/Files/resource I/O.
    // The selected provider is privately retained and authenticated by the unique original issuer; do not invoke provider factories under leases.
    private bool OriginalBindingCurrent(OriginalDisplaySelection selected)=>selected.Available&&hostAllowsWrites()&&
        authorization.IsIssuedOriginalReadOwnerBinding(selected.Actor,selected.OriginalReadContext!,"canvas.file.open",
            selected.OriginalReadContext!.OriginalScope,selected.Provider,directories,selected.StoreId);

    private Task<FilesWorkspaceDirectoryResolver.FilesOriginalBindingCommitLease?> RetainOriginalBindingAsync(OriginalDisplaySelection selected,CancellationToken ct)
        =>directories.AcquireOriginalBindingCommitLeaseAsync(selected.OriginalBinding!,selected.Provider,ct);

    private sealed class OriginalBindingAuthorityLifetime(CanvasFilesArtifactBridge issuer,OriginalDisplaySelection selected,
        Func<CancellationToken,ValueTask<bool>> validateHome,IAsyncDisposable home):IAsyncDisposable
    {
        private FilesWorkspaceDirectoryResolver.FilesOriginalBindingCommitLease? _binding;
        private bool _disposed;
        private bool OriginalCurrent()=>!_disposed&&issuer.OriginalBindingCurrent(selected);
        internal async ValueTask<bool> ValidateAsync(CancellationToken ct)
        {
            // Executed ONLY by actual Files metadata commit; no provider/resource/Home observation.
            if(!OriginalCurrent())return false;
            _binding??=await issuer.RetainOriginalBindingAsync(selected,ct).ConfigureAwait(false);
            if(_binding is null||!_binding.IsHeld||!OriginalCurrent())return false;
            // Order: Files metadata -> existing binding lease -> cap completion gate -> raw Home.
            return await validateHome(ct).ConfigureAwait(false)&&_binding.IsHeld&&OriginalCurrent();
        }
        public async ValueTask DisposeAsync()
        {
            if(_disposed)return;_disposed=true;
            // Release reverse order, before any caller Home audit/adoption await.
            try{await home.DisposeAsync().ConfigureAwait(false);}
            finally{if(_binding is not null)await _binding.DisposeAsync().ConfigureAwait(false);}
        }
    }
}

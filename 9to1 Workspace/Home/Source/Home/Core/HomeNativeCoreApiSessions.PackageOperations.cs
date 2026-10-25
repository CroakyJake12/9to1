using HavenOS.Home.Apps;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeCoreApiSessions
{
    private HomeNativePackageOperationsService? _originalPackageService;
    internal void BindOriginalPackageService(HomeNativePackageOperationsService original)
    {
        lock (_admissionGate)
        {
            if (_closing || _originalPackageService is not null) throw new InvalidOperationException("The SAME original package service must be composed once before admission.");
            _originalPackageService = original;
        }
    }
    public sealed partial class Session
    {
        internal HomeNativeInstalledPeer OriginalPackagePeer => _context.Peer;
        internal string OriginalPackageSessionId => _context.SessionId;
        internal Haven.Application.AuthenticatedResourceActor OriginalPackageActor => _context.Actor;
        internal async Task DemandOriginalPackageHomeAsync(CancellationToken token)
        {
            await _context.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_issuer._sessions.TryGetValue(_context.Id, out var actual) || !ReferenceEquals(actual, _context) ||
                    !await _issuer.CurrentAsync(_context, token).ConfigureAwait(false) || _context.Peer.AppId != "home" ||
                    !_context.Peer.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole) ||
                    !_context.Peer.AllowedServiceIds.Contains("home.core"))
                    throw new UnauthorizedAccessException("The SAME actual installed Home session is required for package removal.");
            }
            finally { _context.Gate.Release(); }
        }
        internal Task DemandOriginalPackageBorrowedAsync(CancellationToken token) =>
            _issuer.DemandOriginalPackageArtifactSessionCurrentAsync(_context.Actor, _context.Peer, _context.SessionId, token);
        public Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> BeginPackageRemovalAsync(HomePackageActionRequest request, CancellationToken token = default)
            => (_issuer._originalPackageService ?? throw new PlatformNotSupportedException("The actual package owner is unavailable.")).BeginAsync(this, request, token);
        public Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> ContinuePackageRemovalAsync(string operationId, CancellationToken token = default)
            => (_issuer._originalPackageService ?? throw new PlatformNotSupportedException("The actual package owner is unavailable.")).ContinueAsync(this, operationId, token);
        public Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> ObservePackageRemovalAsync(string operationId, CancellationToken token = default)
            => (_issuer._originalPackageService ?? throw new PlatformNotSupportedException("The actual package owner is unavailable.")).ObserveAsync(this, operationId, token);
        public Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> RecoverPackageRemovalAsync(string operationId, CancellationToken token = default)
            => (_issuer._originalPackageService ?? throw new PlatformNotSupportedException("The actual package owner is unavailable.")).RecoverAsync(this, operationId, token);
    }
}

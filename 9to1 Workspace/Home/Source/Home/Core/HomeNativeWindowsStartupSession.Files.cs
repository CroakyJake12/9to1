namespace HavenOS.Home.Core;
public sealed partial class HomeNativeWindowsStartupSession
{
    internal Task<HomeNativeFilesReply> InvokeOriginalFilesAsync(HomeNativeFilesRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_request.AppId != HomeNativeFilesActionPolicies.TargetAppId ||
                !_request.RequiredServices.Any(row => row.Required &&
                    row.ServiceId == HomeNativeFilesActionPolicies.RequiredInstalledServiceId))
                throw new UnauthorizedAccessException("Files requires the trusted original app and required service declaration.");
            return _client.InvokeOriginalFilesAsync(request, token);
        }
    }
}

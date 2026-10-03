using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

/// <summary>Actual owning NativeFiles configuration and opaque physical display are retained before Home review.</summary>
public sealed class CanvasHomeOriginalInsertionDisplay : IDisposable
{
    private readonly HomeCoreStateRecord _configuration;
    private CanvasHomeOriginalInsertionDisplay(CanvasHomeClaimedNativeInsertionStore issuer, HostedItemId fileId, CanvasFilesOpenResult opened,
        AuthenticatedResourceActor actor, DurableDriveProvider provider, HomeCoreStateRecord configuration, IOriginalCanonicalWriteContext originalWrite)
    {
        Issuer=issuer;FileId=fileId;Opened=opened;Actor=actor;Provider=provider;OriginalWrite=originalWrite;
        _configuration=configuration with {Payload=configuration.Payload.Clone()};
        ConfigurationHash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_configuration)));
    }
    internal CanvasHomeClaimedNativeInsertionStore Issuer {get;}
    internal HostedItemId FileId {get;}
    internal CanvasFilesOpenResult Opened {get;}
    internal AuthenticatedResourceActor Actor {get;}
    internal DurableDriveProvider Provider {get;}
    internal string ConfigurationHash {get;}
    internal IOriginalCanonicalWriteContext OriginalWrite {get;}
    internal HomeCoreStateRecord CopyConfiguration()=>_configuration with {Payload=_configuration.Payload.Clone()};
    internal static async Task<CanvasHomeOriginalInsertionDisplay> CaptureAsync(CanvasHomeClaimedNativeInsertionStore issuer, CanvasFilesArtifactBridge files,
        NativeFilesWorkspaceAuthority authority, HostedItemId fileId, Guid originalStoreId,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(issuer);ArgumentNullException.ThrowIfNull(files);ArgumentNullException.ThrowIfNull(authority);ArgumentNullException.ThrowIfNull(originalActor);
        var workspace=await authority.GetCurrentAsync(originalStoreId,cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Original native Files workspace unavailable.");
        if(workspace.Actor!=originalActor)throw new UnauthorizedAccessException("Original native Files actor changed.");
        files.RequireOriginalWorkspaceDirectories(workspace.Directories);
        ICanvasOriginalDisplaySelection? selection=null;
        bool Current()=>issuer.OriginalHostCurrent() && (selection is null || files.IsOriginalDisplayAvailable(selection));
        var context=await issuer.CaptureOriginalReadAsync(workspace,fileId,Current,cancellationToken).ConfigureAwait(false);
        var opened=await files.OpenForDisplayAsync(fileId,originalStoreId,originalActor,workspace.Provider,context,cancellationToken).ConfigureAwait(false);
        selection=opened.OriginalSelection;
        try
        {
            files.RequireOriginalDisplayProvider(opened.OriginalSelection!,workspace.Provider,originalActor,fileId,originalStoreId);
            var configuration=await authority.CaptureOriginalConfigurationRecordAsync(workspace,cancellationToken).ConfigureAwait(false);
            if(!Current())throw new UnauthorizedAccessException("Original Canvas display retired during capture.");
            var originalWrite=await issuer.CaptureOriginalWriteAsync(context,cancellationToken).ConfigureAwait(false);
            if(!Current())throw new UnauthorizedAccessException("Original Canvas display retired during write capture.");
            return new(issuer,fileId,opened,originalActor,workspace.Provider,configuration,originalWrite);
        }
        catch {opened.OriginalSelection?.Dispose();throw;}
    }
    public void Dispose()=>Opened.OriginalSelection?.Dispose();
}

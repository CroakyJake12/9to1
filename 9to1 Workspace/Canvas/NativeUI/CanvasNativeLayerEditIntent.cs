using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
namespace HavenOS.Apps.Canvas;

public enum CanvasNativeLayerEditKind { Create, Rename, Reorder, MoveStroke, SetVisibility, SetLocked, Delete }
/// <summary>Typed scalar command. This record and its prepared bytes are proposals, never authority.</summary>
public sealed record CanvasNativeLayerEditCommand(CanvasNativeLayerEditKind Kind, Guid PageId, Guid LayerId,
    string? Name = null, int? Position = null, Guid? StrokeId = null, bool? Value = null);

public sealed class CanvasNativeLayerEditIntent
{
    private readonly byte[] _prepared;
    private readonly JsonElement _arguments;
    private readonly HomeCoreStateRecord _configuration;
    internal CanvasNativeLayerEditIntent(CanvasHomeClaimedNativeLayerStore issuer,
        CanvasHomeOriginalInsertionDisplay display, CanvasNativeLayerEditCommand command)
    {
        Issuer=issuer;Display=display;Command=command;OperationId=Guid.NewGuid();
        var original=display.Opened;
        FileId=display.FileId;StoreId=original.StoreId;FilesRevision=original.CasRevisionId;
        OriginalActor=display.Actor;ArtifactId=original.Artifact.ArtifactId;OriginalRevisionId=original.Artifact.RevisionId;
        _configuration=display.CopyConfiguration();OriginalConfigurationHash=display.ConfigurationHash;
        if(FileId.Value==Guid.Empty||StoreId==Guid.Empty||FilesRevision.Value==Guid.Empty)
            throw new ArgumentException("Layer edit requires original canonical Files provenance.");
        var baseline=CanvasArtifactCodec.Serialize(original.Artifact);
        OriginalContentHash=Hash(baseline);
        using var candidate=CanvasRnoteDocument.Open(baseline);
        var request=new CanvasMutationRequest(OriginalRevisionId,OperationId,
            new(OriginalActor.ActorId,OriginalActor.ActorId,"Original native layer proposal"));
        switch(command.Kind)
        {
            case CanvasNativeLayerEditKind.Create:
                candidate.CreateNativeUserLayer(command.PageId,command.LayerId,RequiredName(command),command.Position,request);break;
            case CanvasNativeLayerEditKind.Rename:
                candidate.RenameNativeUserLayer(command.PageId,command.LayerId,RequiredName(command),request);break;
            case CanvasNativeLayerEditKind.Reorder:
                candidate.ReorderNativeUserLayer(command.PageId,command.LayerId,command.Position??throw new ArgumentException("Layer position required."),request);break;
            case CanvasNativeLayerEditKind.MoveStroke:
                candidate.MoveNativeStrokeToUserLayer(command.PageId,command.StrokeId??throw new ArgumentException("Original stroke required."),command.LayerId,request);break;
            case CanvasNativeLayerEditKind.SetVisibility:
                candidate.SetNativeUserLayerVisibility(command.PageId,command.LayerId,RequiredValue(command),request);break;
            case CanvasNativeLayerEditKind.SetLocked:
                candidate.SetNativeUserLayerLocked(command.PageId,command.LayerId,RequiredValue(command),request);break;
            case CanvasNativeLayerEditKind.Delete:
                if(command.Name is not null || command.Position is not null || command.StrokeId is not null || command.Value is not null)
                    throw new ArgumentException("Exact layer deletion accepts only its original page and layer identity.");
                candidate.DeleteNativeUserLayer(command.PageId,command.LayerId,request);break;
            default:throw new ArgumentOutOfRangeException(nameof(command));
        }
        _prepared=candidate.Serialize();PreparedHash=Hash(_prepared);
        if(candidate.Identity.ArtifactId!=ArtifactId||candidate.Identity.RevisionId==OriginalRevisionId)
            throw new InvalidOperationException("Native layer proposal has no original canonical effect.");
        Scopes=Array.AsReadOnly(new[]{new ResourceScope("files.item",FileId.ToString(),FilesRevision.ToString(),ResourceAccess.Write)});
        _arguments=JsonSerializer.SerializeToElement(new {operation="layer.edit.native",FileId=FileId.Value,StoreId,
            expectedFilesRevision=FilesRevision.Value,ArtifactId,expectedArtifactRevision=OriginalRevisionId,
            originalActor=OriginalActor.ActorId,originalProfile=OriginalActor.ProfileId,OriginalContentHash,
            OperationId,command,PreparedHash,OriginalConfigurationHash},new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    private static string RequiredName(CanvasNativeLayerEditCommand command)=>command.Name??throw new ArgumentException("Layer name required.");
    private static bool RequiredValue(CanvasNativeLayerEditCommand command)=>command.Value??throw new ArgumentException("Layer state required.");
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal CanvasHomeClaimedNativeLayerStore Issuer{get;}
    internal CanvasHomeOriginalInsertionDisplay Display{get;}
    internal byte[] CopyPrepared()=>_prepared.ToArray();
    internal HomeCoreStateRecord CopyConfiguration()=>_configuration with {Payload=_configuration.Payload.Clone()};
    public CanvasNativeLayerEditCommand Command{get;}
    public Guid OperationId{get;}
    public HostedItemId FileId{get;} public Guid StoreId{get;} public FilesRevisionId FilesRevision{get;}
    public Guid ArtifactId{get;} public Guid OriginalRevisionId{get;}
    public AuthenticatedResourceActor OriginalActor{get;}
    public string OriginalContentHash{get;} public string PreparedHash{get;} public string OriginalConfigurationHash{get;}
    public IReadOnlyList<ResourceScope> Scopes{get;}
    public JsonElement Arguments=>_arguments.Clone();
}

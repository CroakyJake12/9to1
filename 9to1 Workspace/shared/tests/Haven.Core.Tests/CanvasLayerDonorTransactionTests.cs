using Haven.Application;
using Haven.Application.Canvas;
namespace Haven.Core.Tests;
/// <summary>Actual canonical owner semantics; callbacks are controlled, not native/Home/Files grants.</summary>
public sealed class CanvasLayerDonorTransactionTests
{
    [Fact]
    public void Exact_original_layer_reorder_captures_once_denies_reentry_and_preserves_history_on_replay()
    {
        var artifact = CanvasArtifact.Create("Layer transaction");
        var page = artifact.Pages[0]; var second = new CanvasLayer { Name = page.Layers[0].Name };
        artifact.Pages[0] = page with { Layers = [page.Layers[0], second], LayerOrder = [page.Layers[0].LayerId, second.LayerId] };
        var session = new CanvasArtifactSession(artifact);var before = session.GetArtifactSnapshot();
        var request = Request(session);var calls = 0;
        CanvasDocumentSettings Capture(CanvasArtifact proposal)
        {
            ++calls;Assert.Equal(new[]{second.LayerId,page.Layers[0].LayerId},proposal.Pages[0].LayerOrder);
            Assert.False(session.RenameArtifact(Request(session),"Nested mutation").IsSuccess);
            proposal.Pages[0].LayerOrder.Reverse(); // Alias cannot alter privately frozen proposal.
            return proposal.DocumentSettings;
        }
        Assert.True(session.ReorderLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,1,Capture).IsSuccess);
        var committed = session.GetArtifactSnapshot();
        Assert.Equal(new[]{second.LayerId,page.Layers[0].LayerId},committed.Pages[0].LayerOrder);
        Assert.Equal(before.DisplayName,committed.DisplayName);var bytes=CanvasArtifactCodec.Serialize(committed);
        Assert.True(session.ReorderLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,1,Capture).IsSuccess);
        Assert.Equal(1,calls);Assert.Equal(bytes,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var reopened=new CanvasArtifactSession(CanvasArtifactCodec.Deserialize(bytes));
        Assert.True(reopened.Undo(Request(reopened)).IsSuccess);
        Assert.Equal(before.Pages[0].LayerOrder,reopened.GetArtifactSnapshot().Pages[0].LayerOrder);
        Assert.True(reopened.Redo(Request(reopened)).IsSuccess);
        Assert.Equal(committed.Pages[0].LayerOrder,reopened.GetArtifactSnapshot().Pages[0].LayerOrder);
    }
    [Fact]
    public void Refused_native_preparation_leaves_actual_canonical_bytes_and_retry_available()
    {
        var artifact=CanvasArtifact.Create("Layer refusal");var page=artifact.Pages[0];var second=new CanvasLayer{Name="Second"};
        artifact.Pages[0]=page with{Layers=[page.Layers[0],second],LayerOrder=[page.Layers[0].LayerId,second.LayerId]};
        var session=new CanvasArtifactSession(artifact);var before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());var request=Request(session);
        Assert.Throws<IOException>(()=>session.ReorderLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,1,_=>throw new IOException("Actual preparation refused")));
        Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.ReorderLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,1,proposal=>proposal.DocumentSettings).IsSuccess);
    }
    [Fact]
    public void New_layer_uses_exact_captured_identity_and_refusals_do_not_capture_or_change_owner()
    {
        var session=new CanvasArtifactSession(CanvasArtifact.Create("Exact new layer"));var page=session.GetArtifactSnapshot().Pages[0];
        var id=Guid.NewGuid();var request=Request(session);var calls=0;
        CanvasDocumentSettings Capture(CanvasArtifact proposed)
        {
            ++calls;Assert.Equal(id,proposed.Pages[0].LayerOrder[0]);
            Assert.Equal("Exact",proposed.Pages[0].Layers.Single(layer=>layer.LayerId==id).Name);
            return proposed.DocumentSettings;
        }
        Assert.True(session.CreateLayerWithDonor(request,page.PageId,id," Exact ",0,Capture).IsSuccess);
        var bytes=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.CreateLayerWithDonor(request,page.PageId,id," Exact ",0,Capture).IsSuccess);
        Assert.Equal(1,calls);Assert.Equal(bytes,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.False(session.CreateLayerWithDonor(Request(session),page.PageId,id,"Duplicate",0,Capture).IsSuccess);
        Assert.False(session.CreateLayerWithDonor(Request(session),page.PageId,Guid.NewGuid(),"Outside",-1,Capture).IsSuccess);
        Assert.Equal(1,calls);Assert.Equal(bytes,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
    }
    [Fact]
    public void Canonical_ink_layer_move_denies_both_locked_sides_then_captures_exact_once_and_replays_history()
    {
        var artifact=CanvasArtifact.Create("Canonical ink layers");var page=artifact.Pages[0];var origin=page.Layers[0].LayerId;var destination=new CanvasLayer{Name="Destination"};
        artifact.Pages[0]=page with{Layers=[page.Layers[0],destination],LayerOrder=[origin,destination.LayerId]};
        var session=new CanvasArtifactSession(artifact);var stroke=new CanvasInkStroke{LayerId=origin,Samples=[new(0,0,.5),new(10,10,.5)]};
        Assert.True(session.AddStructuredStroke(Request(session),page.PageId,stroke).IsSuccess);var calls=0;
        CanvasDocumentSettings Capture(CanvasArtifact proposal)
        {
            ++calls;Assert.Equal(destination.LayerId,Assert.Single(proposal.Pages[0].Strokes).LayerId);
            proposal.Pages[0].Strokes.Clear(); // callback alias cannot erase canonical ink.
            return proposal.DocumentSettings;
        }
        foreach(var locked in new[]{origin,destination.LayerId})
        {
            Assert.True(session.SetLayerLocked(Request(session),page.PageId,locked,true).IsSuccess);var before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
            Assert.False(session.MoveStrokeToLayerWithDonor(Request(session),page.PageId,stroke.StrokeId,destination.LayerId,Capture).IsSuccess);
            Assert.Equal(0,calls);Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
            Assert.True(session.SetLayerLocked(Request(session),page.PageId,locked,false).IsSuccess);
        }
        var request=Request(session);Assert.True(session.MoveStrokeToLayerWithDonor(request,page.PageId,stroke.StrokeId,destination.LayerId,Capture).IsSuccess);
        var result=session.GetArtifactSnapshot();Assert.Equal(stroke.StrokeId,Assert.Single(result.Pages[0].Strokes).StrokeId);Assert.Equal(destination.LayerId,result.Pages[0].Strokes[0].LayerId);
        var bytes=CanvasArtifactCodec.Serialize(result);Assert.True(session.MoveStrokeToLayerWithDonor(request,page.PageId,stroke.StrokeId,destination.LayerId,Capture).IsSuccess);
        Assert.Equal(1,calls);Assert.Equal(bytes,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var reopened=new CanvasArtifactSession(CanvasArtifactCodec.Deserialize(bytes));Assert.True(reopened.Undo(Request(reopened)).IsSuccess);Assert.Equal(origin,Assert.Single(reopened.GetArtifactSnapshot().Pages[0].Strokes).LayerId);
        Assert.True(reopened.Redo(Request(reopened)).IsSuccess);Assert.Equal(destination.LayerId,Assert.Single(reopened.GetArtifactSnapshot().Pages[0].Strokes).LayerId);
    }
    [Fact]
    public void Layer_state_capture_is_atomic_noop_and_existing_typed_api_replay_match_the_original_tuple()
    {
        var session=new CanvasArtifactSession(CanvasArtifact.Create("Original layer state"));var page=session.GetArtifactSnapshot().Pages[0];var layer=page.Layers[0];var calls=0;
        CanvasDocumentSettings Capture(CanvasArtifact proposed)
        {
            ++calls;Assert.False(proposed.Pages[0].Layers[0].IsVisible);
            proposed.Pages[0].Layers[0]=proposed.Pages[0].Layers[0] with{Name="Callback alias"};return proposed.DocumentSettings;
        }
        var before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.SetLayerVisibilityWithDonor(Request(session),page.PageId,layer.LayerId,true,Capture).IsSuccess);
        Assert.False(session.SetLayerVisibilityWithDonor(Request(session),page.PageId,Guid.NewGuid(),false,Capture).IsSuccess);
        Assert.Equal(0,calls);Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var request=Request(session);Assert.True(session.SetLayerVisibilityWithDonor(request,page.PageId,layer.LayerId,false,Capture).IsSuccess);
        var hidden=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());Assert.Equal(layer.Name,session.GetArtifactSnapshot().Pages[0].Layers[0].Name);
        Assert.True(session.SetLayerVisibility(request,page.PageId,layer.LayerId,false).IsSuccess); // exact existing action/payload fingerprint.
        Assert.Equal(1,calls);Assert.Equal(hidden,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var lockRequest=Request(session);Assert.Throws<IOException>(()=>session.SetLayerLockedWithDonor(lockRequest,page.PageId,layer.LayerId,true,_=>throw new IOException("Native map capture refused")));
        Assert.Equal(hidden,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.SetLayerLockedWithDonor(lockRequest,page.PageId,layer.LayerId,true,proposal=>proposal.DocumentSettings).IsSuccess);
        var reopened=new CanvasArtifactSession(CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.True(reopened.GetArtifactSnapshot().Pages[0].Layers[0].IsLocked);Assert.True(reopened.Undo(Request(reopened)).IsSuccess);
        Assert.False(reopened.GetArtifactSnapshot().Pages[0].Layers[0].IsLocked);Assert.False(reopened.GetArtifactSnapshot().Pages[0].Layers[0].IsVisible);
    }
    [Fact]
    public void Exact_layer_delete_removes_its_ink_once_and_codec_reopen_restores_membership_with_history()
    {
        var artifact=CanvasArtifact.Create("Delete layer");var page=artifact.Pages[0];var second=new CanvasLayer{Name="Retained"};
        artifact.Pages[0]=page with{Layers=[page.Layers[0],second],LayerOrder=[page.Layers[0].LayerId,second.LayerId]};
        var session=new CanvasArtifactSession(artifact);
        var removed=new CanvasInkStroke{LayerId=page.Layers[0].LayerId,Samples=[new(0,0,.5),new(10,10,.5)]};
        var retained=new CanvasInkStroke{LayerId=second.LayerId,Samples=[new(20,20,.5),new(30,30,.5)]};
        Assert.True(session.AddStructuredStroke(Request(session),page.PageId,removed).IsSuccess);
        Assert.True(session.AddStructuredStroke(Request(session),page.PageId,retained).IsSuccess);
        var before=session.GetArtifactSnapshot();var request=Request(session);var calls=0;
        CanvasDocumentSettings Capture(CanvasArtifact proposed)
        {
            ++calls;Assert.Equal(second.LayerId,Assert.Single(proposed.Pages[0].Layers).LayerId);
            Assert.Equal(retained.StrokeId,Assert.Single(proposed.Pages[0].Strokes).StrokeId);
            proposed.Pages[0].Strokes.Clear();return proposed.DocumentSettings;
        }
        Assert.True(session.DeleteLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,Capture).IsSuccess);
        Assert.Equal(retained.StrokeId,Assert.Single(session.GetArtifactSnapshot().Pages[0].Strokes).StrokeId);
        var bytes=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.True(session.DeleteLayerWithDonor(request,page.PageId,page.Layers[0].LayerId,Capture).IsSuccess);
        Assert.Equal(1,calls);Assert.Equal(bytes,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var reopened=new CanvasArtifactSession(CanvasArtifactCodec.Deserialize(bytes));
        Assert.True(reopened.Undo(Request(reopened)).IsSuccess);
        Assert.Equal(before.Pages[0].LayerOrder,reopened.GetArtifactSnapshot().Pages[0].LayerOrder);
        Assert.Equal(before.Pages[0].StrokeOrder,reopened.GetArtifactSnapshot().Pages[0].StrokeOrder);
        Assert.True(reopened.Redo(Request(reopened)).IsSuccess);
        Assert.Equal(retained.StrokeId,Assert.Single(reopened.GetArtifactSnapshot().Pages[0].Strokes).StrokeId);
    }
    [Fact]
    public void Locked_last_missing_layer_and_failed_donor_delete_preserve_bytes_and_exact_retry()
    {
        var session=new CanvasArtifactSession(CanvasArtifact.Create("Delete refusal"));var page=session.GetArtifactSnapshot().Pages[0];var layer=page.Layers[0].LayerId;var calls=0;
        CanvasDocumentSettings Capture(CanvasArtifact proposed){++calls;return proposed.DocumentSettings;}
        var before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.False(session.DeleteLayerWithDonor(Request(session),page.PageId,layer,Capture).IsSuccess);
        Assert.False(session.DeleteLayerWithDonor(Request(session),page.PageId,Guid.NewGuid(),Capture).IsSuccess);
        Assert.Equal(0,calls);Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.CreateLayerWithDonor(Request(session),page.PageId,Guid.NewGuid(),"Retained",null,Capture).IsSuccess);
        Assert.True(session.SetLayerLocked(Request(session),page.PageId,layer,true).IsSuccess);before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());var count=calls;
        Assert.False(session.DeleteLayerWithDonor(Request(session),page.PageId,layer,Capture).IsSuccess);
        Assert.Equal(count,calls);Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.SetLayerLocked(Request(session),page.PageId,layer,false).IsSuccess);before=CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());var request=Request(session);
        Assert.Throws<IOException>(()=>session.DeleteLayerWithDonor(request,page.PageId,layer,_=>throw new IOException("Donor delete failed")));
        Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.DeleteLayerWithDonor(request,page.PageId,layer,Capture).IsSuccess);
    }
    private static CanvasMutationRequest Request(CanvasArtifactSession session)=>new(session.CurrentRevisionId,Guid.NewGuid(),new("layer-fixture","Layer fixture"));
}

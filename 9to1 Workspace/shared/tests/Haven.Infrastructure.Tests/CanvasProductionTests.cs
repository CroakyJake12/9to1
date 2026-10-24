using System.Text.Json;
using Haven.Application;
using Haven.Application.Canvas;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CanvasProductionTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Shared_object_history_reports_removed_and_restored_canonical_identity()
    {
        var artifact = CanvasArtifact.Create("Shared history");
        var session = new CanvasArtifactSession(artifact);
        var page = artifact.Pages.Single();
        var layer = page.Layers.Single();
        using var payloadDocument = JsonDocument.Parse("{\"text\":\"owned elsewhere\"}");
        var sourceReference = new CanvasSharedObjectReference("9to1.Write", Guid.NewGuid(),
            Guid.NewGuid(), "9to1.Write.TextBlock", Guid.NewGuid());
        var sharedObject = new CanvasObject
        {
            ObjectId = Guid.NewGuid(), LayerId = layer.LayerId,
            ObjectTypeId = "9to1.Write.TextBlock", SharedPayload = payloadDocument.RootElement.Clone(),
            SharedObjectRef = sourceReference
        };
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        var inserted = session.InsertSharedObjects(Request(), page.PageId, [sharedObject]);
        Assert.True(inserted.IsSuccess);
        var canonical = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sharedObject.ObjectId, canonical.ObjectId);
        // Reopening the canonical artifact retains its history and opaque source-app
        // reference; storage ownership stays outside the Canvas session.
        session = new CanvasArtifactSession(CanvasArtifactCodec.Deserialize(
            CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));

        var undone = session.Undo(Request());
        Assert.True(undone.IsSuccess);
        Assert.Contains(sharedObject.ObjectId, undone.Value!.ChangedIds);
        var events = session.GetChanges();
        Assert.True(events.IsSuccess);
        Assert.Contains(sharedObject.ObjectId, Assert.Single(events.Value!.Events).ChangedIds);
        Assert.Empty(session.GetArtifactSnapshot().Pages.Single().Objects);

        var revisionBeforeRedo = session.CurrentRevisionId;
        var redoRequest = Request();
        var redone = session.Redo(redoRequest);
        Assert.True(redone.IsSuccess);
        Assert.Contains(sharedObject.ObjectId, redone.Value!.ChangedIds);
        var redoEvents = session.GetChanges(events.Value!.NextCursor);
        Assert.True(redoEvents.IsSuccess);
        Assert.Contains(sharedObject.ObjectId, Assert.Single(redoEvents.Value!.Events).ChangedIds);
        var revisionAfterRedo = session.CurrentRevisionId;
        var retriedRedo = session.Redo(redoRequest);
        Assert.True(retriedRedo.IsSuccess);
        Assert.Equal(redone.Value.RevisionId, retriedRedo.Value!.RevisionId);
        Assert.Equal(revisionAfterRedo, session.CurrentRevisionId);
        var afterRetry = session.GetChanges(redoEvents.Value!.NextCursor);
        Assert.True(afterRetry.IsSuccess);
        Assert.Empty(afterRetry.Value!.Events);

        var staleUndo = session.Undo(new(revisionBeforeRedo, Guid.NewGuid(),
            new("canvas-test", "Canvas test")));
        Assert.False(staleUndo.IsSuccess);
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, staleUndo.Error!.Code);
        Assert.Equal(revisionAfterRedo, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(afterRetry.Value!.NextCursor).Value!.Events);

        var restoredObject = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sharedObject.ObjectId, restoredObject.ObjectId);
        Assert.Equal("owned elsewhere", restoredObject.SharedPayload!.Value.GetProperty("text").GetString());
        var originalRevision = session.CurrentRevisionId;
        var retargeted = session.UpdateSharedObject(Request(), page.PageId,
            restoredObject with { SharedObjectRef = sourceReference with { ArtifactId = Guid.NewGuid() } });
        Assert.False(retargeted.IsSuccess);
        Assert.Equal(CanvasApiErrorCode.UnsupportedFeature, retargeted.Error!.Code);
        var detachedReference = session.UpdateSharedObject(Request(), page.PageId,
            restoredObject with { SharedObjectRef = null });
        Assert.False(detachedReference.IsSuccess);
        Assert.Equal(CanvasApiErrorCode.UnsupportedFeature, detachedReference.Error!.Code);
        using var changedPayloadDocument = JsonDocument.Parse("{\"text\":\"unauthorised local edit\"}");
        var editedReferencedContent = session.UpdateSharedObject(Request(), page.PageId,
            restoredObject with { SharedPayload = changedPayloadDocument.RootElement.Clone() });
        Assert.False(editedReferencedContent.IsSuccess);
        Assert.Equal(CanvasApiErrorCode.UnsupportedFeature, editedReferencedContent.Error!.Code);
        Assert.Equal(sourceReference,
            Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects).SharedObjectRef);
        Assert.Equal("owned elsewhere", Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects)
            .SharedPayload!.Value.GetProperty("text").GetString());
        Assert.Equal(originalRevision, session.CurrentRevisionId);
        var moved = session.UpdateSharedObject(Request(), page.PageId,
            restoredObject with { Geometry = restoredObject.Geometry with { X = 42 } });
        Assert.True(moved.IsSuccess);
        var movedObject = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sharedObject.ObjectId, movedObject.ObjectId);
        Assert.Equal(42, movedObject.Geometry.X);
        Assert.Equal(sourceReference, movedObject.SharedObjectRef);

        var cursorBeforeDelete = session.GetChanges().Value!.NextCursor;
        var deleteRequest = Request();
        var deleted = session.DeleteObject(deleteRequest, page.PageId, sharedObject.ObjectId);
        Assert.True(deleted.IsSuccess);
        var deleteResult = deleted.Value!;
        Assert.Contains(sharedObject.ObjectId, deleteResult.ChangedIds);
        Assert.DoesNotContain(sourceReference.ObjectId, deleteResult.ChangedIds);
        Assert.Empty(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Empty(session.GetArtifactSnapshot().Pages.Single().ObjectOrder);
        var deleteEvents = session.GetChanges(cursorBeforeDelete);
        Assert.True(deleteEvents.IsSuccess);
        var deleteEvent = Assert.Single(deleteEvents.Value!.Events);
        Assert.Contains(sharedObject.ObjectId, deleteEvent.ChangedIds);
        Assert.DoesNotContain(sourceReference.ObjectId, deleteEvent.ChangedIds);
        var revisionAfterDelete = session.CurrentRevisionId;
        var replayedDelete = session.DeleteObject(deleteRequest, page.PageId, sharedObject.ObjectId);
        Assert.True(replayedDelete.IsSuccess);
        Assert.Equal(deleteResult.RevisionId, replayedDelete.Value!.RevisionId);
        Assert.Equal(revisionAfterDelete, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(deleteEvents.Value!.NextCursor).Value!.Events);
        var restored = session.Undo(Request());
        Assert.True(restored.IsSuccess);
        Assert.Contains(sharedObject.ObjectId, restored.Value!.ChangedIds);
        var recoveredObject = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sharedObject.ObjectId, recoveredObject.ObjectId);
        Assert.Equal(new[] { sharedObject.ObjectId }, session.GetArtifactSnapshot().Pages.Single().ObjectOrder);
        Assert.Equal(sourceReference, recoveredObject.SharedObjectRef);
        Assert.Equal("owned elsewhere", recoveredObject.SharedPayload!.Value.GetProperty("text").GetString());
        Assert.True(session.Redo(Request()).IsSuccess);
        Assert.Empty(session.GetArtifactSnapshot().Pages.Single().Objects);
    }

    [Fact]
    public void Referenced_object_placement_history_round_trip_preserves_owner_identity_and_payload()
    {
        var artifact = CanvasArtifact.Create("Referenced placement history");
        var page = artifact.Pages.Single();
        var layer = page.Layers.Single();
        const string opaquePayload = "{\"text\":\"owner content\",\"future\":{\"value\":7}}";
        using var payloadDocument = JsonDocument.Parse(opaquePayload);
        var sourceReference = new CanvasSharedObjectReference("9to1.Write", Guid.NewGuid(),
            Guid.NewGuid(), "9to1.Write.TextBlock", Guid.NewGuid());
        var referencedObject = new CanvasObject
        {
            LayerId = layer.LayerId,
            ObjectTypeId = sourceReference.ObjectTypeId,
            SharedObjectRef = sourceReference,
            SharedPayload = payloadDocument.RootElement.Clone()
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        var inserted = session.InsertSharedObjects(Request(), page.PageId, [referencedObject]);
        Assert.True(inserted.IsSuccess);
        var original = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        var movedGeometry = original.Geometry with { X = 75, Y = 40 };
        var placementUpdate = session.UpdateSharedObject(Request(), page.PageId,
            original with { Geometry = movedGeometry });
        Assert.True(placementUpdate.IsSuccess);

        CanvasArtifactSession Reopen(CanvasArtifactSession current) => new(CanvasArtifactCodec.Deserialize(
            CanvasArtifactCodec.Serialize(current.GetArtifactSnapshot())));
        void AssertPlacement(CanvasRect expectedGeometry)
        {
            var value = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
            Assert.Equal(referencedObject.ObjectId, value.ObjectId);
            Assert.Equal(expectedGeometry, value.Geometry);
            Assert.Equal(sourceReference, value.SharedObjectRef);
            Assert.Equal(opaquePayload, value.SharedPayload!.Value.GetRawText());
        }

        session = Reopen(session);
        AssertPlacement(movedGeometry);
        Assert.True(session.Undo(Request()).IsSuccess);
        session = Reopen(session);
        AssertPlacement(original.Geometry);
        Assert.True(session.Redo(Request()).IsSuccess);
        session = Reopen(session);
        AssertPlacement(movedGeometry);

        Assert.True(session.SetLayerLocked(Request(), page.PageId, layer.LayerId, true).IsSuccess);
        var lockedRevision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var beforeDeniedEdit = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var deniedEdit = session.UpdateSharedObject(Request(), page.PageId,
            Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects) with
            { Geometry = movedGeometry with { X = 300 } });
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, deniedEdit.Error!.Code);
        Assert.Equal(lockedRevision, session.CurrentRevisionId);
        Assert.Equal(beforeDeniedEdit,
            Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        var noDeniedEvent = session.GetChanges(eventCursor);
        Assert.True(noDeniedEvent.IsSuccess);
        Assert.Empty(noDeniedEvent.Value!.Events);
        AssertPlacement(movedGeometry);
    }

    [Fact]
    public void Duplicating_referenced_object_creates_new_placement_without_cloning_owner_content()
    {
        var artifact = CanvasArtifact.Create("Referenced duplicate");
        var page = artifact.Pages.Single();
        var layer = page.Layers.Single();
        const string opaquePayload = "{\"text\":\"owner content\",\"future\":{\"value\":7}}";
        using var payloadDocument = JsonDocument.Parse(opaquePayload);
        var sourceReference = new CanvasSharedObjectReference("9to1.Write", Guid.NewGuid(),
            Guid.NewGuid(), "9to1.Write.TextBlock", Guid.NewGuid());
        var sourceObject = new CanvasObject
        {
            LayerId = layer.LayerId,
            ObjectTypeId = sourceReference.ObjectTypeId,
            SharedObjectRef = sourceReference,
            SharedPayload = payloadDocument.RootElement.Clone()
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        Assert.True(session.InsertSharedObjects(Request(), page.PageId, [sourceObject]).IsSuccess);
        var duplicateGeometry = new CanvasRect(120, 80, 240, 130);
        var duplicated = session.DuplicateObject(Request(), page.PageId, sourceObject.ObjectId, duplicateGeometry);
        Assert.True(duplicated.IsSuccess);
        var duplicateId = Assert.Single(duplicated.Value!.ChangedIds.Where(id => id != page.PageId));
        Assert.NotEqual(sourceObject.ObjectId, duplicateId);
        CanvasArtifactSession Reopen(CanvasArtifactSession current) => new(CanvasArtifactCodec.Deserialize(
            CanvasArtifactCodec.Serialize(current.GetArtifactSnapshot())));
        void AssertBothPlacements()
        {
            var objects = session.GetArtifactSnapshot().Pages.Single().Objects;
            Assert.Equal(2, objects.Count);
            var canonicalSource = Assert.Single(objects.Where(item => item.ObjectId == sourceObject.ObjectId));
            var duplicate = Assert.Single(objects.Where(item => item.ObjectId == duplicateId));
            Assert.Equal(sourceReference, canonicalSource.SharedObjectRef);
            Assert.Equal(sourceReference, duplicate.SharedObjectRef);
            Assert.Equal(opaquePayload, canonicalSource.SharedPayload!.Value.GetRawText());
            Assert.Equal(opaquePayload, duplicate.SharedPayload!.Value.GetRawText());
            Assert.Equal(duplicateGeometry, duplicate.Geometry);
        }

        session = Reopen(session);
        AssertBothPlacements();
        var undone = session.Undo(Request());
        Assert.True(undone.IsSuccess);
        Assert.Contains(duplicateId, undone.Value!.ChangedIds);
        session = Reopen(session);
        var remaining = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sourceObject.ObjectId, remaining.ObjectId);
        Assert.Equal(sourceReference, remaining.SharedObjectRef);
        Assert.Equal(opaquePayload, remaining.SharedPayload!.Value.GetRawText());

        var redone = session.Redo(Request());
        Assert.True(redone.IsSuccess);
        Assert.Contains(duplicateId, redone.Value!.ChangedIds);
        session = Reopen(session);
        AssertBothPlacements();
    }

    [Fact]
    public void Listing_objects_is_bounded_page_scoped_ordered_and_detached()
    {
        var artifact = CanvasArtifact.Create("Object listing");
        var page = artifact.Pages.Single();
        var layerId = page.Layers.Single().LayerId;
        using var payload = JsonDocument.Parse("{}");
        using var styleValue = JsonDocument.Parse("\"blue\"");
        CanvasObject CreateObject(string name) => new()
        {
            LayerId = layerId,
            ObjectTypeId = "test.shared/v1",
            Accessibility = new CanvasAccessibilityMetadata { Name = name },
            Style = new CanvasObjectStyle
            {
                Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    { ["tone"] = styleValue.RootElement.Clone() }
            },
            SharedPayload = payload.RootElement.Clone()
        };
        var first = CreateObject("First");
        var second = CreateObject("Second");
        var third = CreateObject("Third");
        artifact.Pages[0] = page with
        {
            Objects = [first, second, third],
            ObjectOrder = [third.ObjectId, first.ObjectId, second.ObjectId]
        };
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;

        var firstPage = session.ListObjects(page.PageId, offset: 1, pageSize: 1);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value!.TotalCount);
        Assert.Equal(2, firstPage.Value.NextOffset);
        var listedFirst = Assert.Single(firstPage.Value.Items);
        Assert.Equal(first.ObjectId, listedFirst.ObjectId);
        Assert.Equal("First", listedFirst.Accessibility.Name);
        listedFirst.Style.Properties.Clear();
        var directRead = session.GetObject(page.PageId, first.ObjectId);
        Assert.True(directRead.IsSuccess);
        directRead.Value!.Style.Properties.Clear();
        Assert.Equal("blue", session.GetObject(page.PageId, first.ObjectId).Value!
            .Style.Properties["tone"].GetString());

        var secondPage = session.ListObjects(page.PageId, offset: 2, pageSize: 1);
        Assert.True(secondPage.IsSuccess);
        Assert.Null(secondPage.Value!.NextOffset);
        Assert.Equal(second.ObjectId, Assert.Single(secondPage.Value.Items).ObjectId);
        var rereadFirst = session.ListObjects(page.PageId, offset: 1, pageSize: 1);
        Assert.True(rereadFirst.IsSuccess);
        Assert.Equal("blue", Assert.Single(rereadFirst.Value!.Items).Style.Properties["tone"].GetString());
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.ListObjects(page.PageId, pageSize: 0).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound,
            session.ListObjects(Guid.NewGuid()).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound,
            session.GetObject(page.PageId, Guid.NewGuid()).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Object_search_uses_typed_metadata_only_and_returns_canonical_paged_hits()
    {
        var artifact = CanvasArtifact.Create("Metadata search");
        var page = artifact.Pages.Single();
        var layerId = page.Layers.Single().LayerId;
        using var opaquePayload = JsonDocument.Parse("{\"text\":\"opaque needle-only-in-payload\"}");
        using var extensionValue = JsonDocument.Parse("\"source-extension\"");
        CanvasObject MakeObject(string type, string name, Dictionary<string, JsonElement>? extensionData = null) => new()
        {
            LayerId = layerId,
            ObjectTypeId = type,
            Accessibility = new CanvasAccessibilityMetadata { Name = name, ExtensionData = extensionData },
            SharedPayload = opaquePayload.RootElement.Clone()
        };
        var first = MakeObject("9to1.Write.TextBlock", "Project plan",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                { ["origin"] = extensionValue.RootElement.Clone() });
        var unknownType = MakeObject("vendor.future-widget/v9", "Project archive");
        var third = MakeObject("9to1.Write.TextBlock", "Meeting notes");
        artifact.Pages[0] = page with
        {
            Objects = [first, unknownType, third],
            ObjectOrder = [unknownType.ObjectId, first.ObjectId, third.ObjectId]
        };
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var before = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));

        var firstPage = session.SearchObjects(new CanvasObjectSearchQuery(page.PageId, Text: "project", PageSize: 1));
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(CanvasObjectSearchCoverage.AccessibilityAndReferenceMetadataOnly, firstPage.Value!.Coverage);
        Assert.Equal(3, firstPage.Value.UnsearchedPayloadObjectCount);
        Assert.Equal(2, firstPage.Value.TotalCount);
        Assert.Equal(1, firstPage.Value.NextOffset);
        var firstHit = Assert.Single(firstPage.Value.Items);
        Assert.Equal(unknownType.ObjectId, firstHit.ObjectId);
        Assert.Equal("vendor.future-widget/v9", firstHit.ObjectTypeId);

        var secondPage = session.SearchObjects(new CanvasObjectSearchQuery(page.PageId, Text: "project", Offset: 1, PageSize: 1));
        Assert.True(secondPage.IsSuccess);
        var secondHit = Assert.Single(secondPage.Value!.Items);
        Assert.Equal(first.ObjectId, secondHit.ObjectId);
        using var mutatedExtension = JsonDocument.Parse("\"mutated result\"");
        secondHit.Accessibility.ExtensionData!["origin"] = mutatedExtension.RootElement.Clone();
        var reread = session.GetObject(page.PageId, first.ObjectId);
        Assert.True(reread.IsSuccess);
        Assert.Equal("source-extension", reread.Value!.Accessibility.ExtensionData!["origin"].GetString());

        var payloadOnly = session.SearchObjects(new CanvasObjectSearchQuery(page.PageId, Text: "needle-only-in-payload"));
        Assert.True(payloadOnly.IsSuccess);
        Assert.Empty(payloadOnly.Value!.Items);
        Assert.Equal(3, payloadOnly.Value.UnsearchedPayloadObjectCount);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.SearchObjects(new CanvasObjectSearchQuery(page.PageId)).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.SearchObjects(new CanvasObjectSearchQuery(page.PageId, Text: "project", PageSize: 0)).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound,
            session.SearchObjects(new CanvasObjectSearchQuery(Guid.NewGuid(), Text: "project")).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Equal(before, Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);

        var canonicalSource = session.GetObject(page.PageId, firstHit.ObjectId).Value!;
        var authorizedEdit = session.UpdateSharedObject(
            new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")),
            page.PageId, canonicalSource with { Geometry = canonicalSource.Geometry with { X = 88 } });
        Assert.True(authorizedEdit.IsSuccess);
        Assert.Equal(first.ObjectId, session.GetObject(page.PageId, first.ObjectId).Value!.ObjectId);
    }

    [Fact]
    public void Selected_object_translation_is_atomic_retry_safe_and_one_undoable_shared_action()
    {
        var artifact = CanvasArtifact.Create("Selected translation");
        var page = artifact.Pages.Single();
        var writable = page.Layers.Single();
        var protectedLayer = new CanvasLayer { Name = "Protected", IsLocked = true };
        using var payload = JsonDocument.Parse("{\"ownerContent\":{\"text\":\"preserve me\"},\"extension\":17}");
        var ownerReference = new CanvasSharedObjectReference("9to1.Write", Guid.NewGuid(),
            Guid.NewGuid(), "9to1.Write.TextBlock", Guid.NewGuid());
        CanvasObject Create(Guid layerId, double x, double y) => new()
        {
            LayerId = layerId,
            ObjectTypeId = "9to1.Write.TextBlock",
            Geometry = new(x, y, 40, 20),
            SharedObjectRef = ownerReference,
            SharedPayload = payload.RootElement.Clone()
        };
        var first = Create(writable.LayerId, 10, 20);
        var second = Create(protectedLayer.LayerId, 110, 70);
        artifact.Pages[0] = page with
        {
            Layers = [writable, protectedLayer],
            LayerOrder = [writable.LayerId, protectedLayer.LayerId],
            Objects = [first, second],
            ObjectOrder = [first.ObjectId, second.ObjectId]
        };
        var deniedSession = new CanvasArtifactSession(artifact);
        var deniedRevision = deniedSession.CurrentRevisionId;
        var deniedCursor = deniedSession.GetChanges().Value!.NextCursor;
        var deniedSnapshot = Convert.ToHexString(CanvasArtifactCodec.Serialize(deniedSession.GetArtifactSnapshot()));
        var denied = deniedSession.TranslateSelectedObjects(
            new(deniedRevision, Guid.NewGuid(), new("canvas-test", "Canvas test")), page.PageId,
            [first.ObjectId, second.ObjectId], 25, -12);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, denied.Error!.Code);
        Assert.Equal(deniedRevision, deniedSession.CurrentRevisionId);
        Assert.Equal(deniedSnapshot, Convert.ToHexString(CanvasArtifactCodec.Serialize(deniedSession.GetArtifactSnapshot())));
        Assert.Empty(deniedSession.GetChanges(deniedCursor).Value!.Events);
        var missing = deniedSession.TranslateSelectedObjects(
            new(deniedRevision, Guid.NewGuid(), new("canvas-test", "Canvas test")), page.PageId,
            [first.ObjectId, Guid.NewGuid()], 25, -12);
        Assert.Equal(CanvasApiErrorCode.NotFound, missing.Error!.Code);
        Assert.Equal(deniedRevision, deniedSession.CurrentRevisionId);
        Assert.Equal(deniedSnapshot, Convert.ToHexString(CanvasArtifactCodec.Serialize(deniedSession.GetArtifactSnapshot())));
        Assert.Empty(deniedSession.GetChanges(deniedCursor).Value!.Events);

        var editablePage = page with
        {
            Layers = [writable, protectedLayer with { IsLocked = false }],
            LayerOrder = [writable.LayerId, protectedLayer.LayerId],
            Objects = [first, second],
            ObjectOrder = [first.ObjectId, second.ObjectId]
        };
        artifact.Pages[0] = editablePage;
        var session = new CanvasArtifactSession(artifact);
        var startRevision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var request = new CanvasMutationRequest(startRevision, Guid.NewGuid(), new("canvas-test", "Canvas test"));
        var ids = new[] { first.ObjectId, second.ObjectId };
        var moved = session.TranslateSelectedObjects(request, page.PageId, ids, 25, -12);
        Assert.True(moved.IsSuccess);
        Assert.Equal(ids, moved.Value!.ChangedObjectIds);
        Assert.Equal(25, moved.Value.DeltaX);
        Assert.Equal(-12, moved.Value.DeltaY);
        Assert.NotEqual(startRevision, moved.Value.RevisionId);

        var movedFirst = session.GetObject(page.PageId, first.ObjectId).Value!;
        var movedSecond = session.GetObject(page.PageId, second.ObjectId).Value!;
        Assert.Equal(new CanvasRect(35, 8, 40, 20), movedFirst.Geometry);
        Assert.Equal(new CanvasRect(135, 58, 40, 20), movedSecond.Geometry);
        Assert.Equal(first.ObjectId, movedFirst.ObjectId);
        Assert.Equal(second.ObjectId, movedSecond.ObjectId);
        Assert.Equal(ownerReference, movedFirst.SharedObjectRef);
        Assert.Equal(ownerReference, movedSecond.SharedObjectRef);
        Assert.Equal(payload.RootElement.GetRawText(), movedFirst.SharedPayload!.Value.GetRawText());
        Assert.Equal(payload.RootElement.GetRawText(), movedSecond.SharedPayload!.Value.GetRawText());

        var retried = session.TranslateSelectedObjects(request, page.PageId, ids, 25, -12);
        Assert.True(retried.IsSuccess);
        Assert.Equal(moved.Value!.RevisionId, retried.Value!.RevisionId);
        Assert.Equal(moved.Value.ChangedObjectIds, retried.Value.ChangedObjectIds);
        Assert.Equal(moved.Value.DeltaX, retried.Value.DeltaX);
        Assert.Equal(moved.Value.DeltaY, retried.Value.DeltaY);
        var oneEvent = Assert.Single(session.GetChanges(eventCursor).Value!.Events);
        Assert.Equal(request.OperationId, oneEvent.OperationId);
        Assert.Equal("Objects.TranslateSelection", oneEvent.Action);
        Assert.Equal(moved.Value.RevisionId, oneEvent.RevisionId);

        var undone = session.Undo(new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")));
        Assert.True(undone.IsSuccess);
        Assert.Equal(new CanvasRect(10, 20, 40, 20), session.GetObject(page.PageId, first.ObjectId).Value!.Geometry);
        Assert.Equal(new CanvasRect(110, 70, 40, 20), session.GetObject(page.PageId, second.ObjectId).Value!.Geometry);
        var redone = session.Redo(new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")));
        Assert.True(redone.IsSuccess);
        Assert.Equal(new CanvasRect(35, 8, 40, 20), session.GetObject(page.PageId, first.ObjectId).Value!.Geometry);
        Assert.Equal(new CanvasRect(135, 58, 40, 20), session.GetObject(page.PageId, second.ObjectId).Value!.Geometry);
    }

    [Fact]
    public void Listing_strokes_is_bounded_page_scoped_ordered_and_detached()
    {
        var artifact = CanvasArtifact.Create("Stroke listing");
        var page = artifact.Pages.Single();
        var layerId = page.Layers.Single().LayerId;
        var first = new CanvasInkStroke { LayerId = layerId, Samples = [new(1, 1), new(2, 2)] };
        var second = new CanvasInkStroke { LayerId = layerId, Samples = [new(3, 3), new(4, 4)] };
        var third = new CanvasInkStroke { LayerId = layerId, Samples = [new(5, 5), new(6, 6)] };
        artifact.Pages[0] = page with
        {
            Strokes = [first, second, third],
            StrokeOrder = [third.StrokeId, first.StrokeId, second.StrokeId]
        };
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;

        var firstPage = session.ListStrokes(page.PageId, offset: 1, pageSize: 1);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value!.TotalCount);
        Assert.Equal(2, firstPage.Value.NextOffset);
        var listedFirst = Assert.Single(firstPage.Value.Items);
        Assert.Equal(first.StrokeId, listedFirst.StrokeId);
        listedFirst.Samples.Clear();

        var secondPage = session.ListStrokes(page.PageId, offset: 2, pageSize: 1);
        Assert.True(secondPage.IsSuccess);
        Assert.Null(secondPage.Value!.NextOffset);
        Assert.Equal(second.StrokeId, Assert.Single(secondPage.Value.Items).StrokeId);
        var rereadFirst = session.ListStrokes(page.PageId, offset: 1, pageSize: 1);
        Assert.True(rereadFirst.IsSuccess);
        Assert.Equal(2, Assert.Single(rereadFirst.Value!.Items).Samples.Count);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.ListStrokes(page.PageId, pageSize: 0).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound,
            session.ListStrokes(Guid.NewGuid()).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Getting_stroke_preserves_detached_path_and_extension_data_without_mutating_session()
    {
        var artifact = CanvasArtifact.Create("Stroke lookup");
        artifact.SchemaVersion = 2;
        var page = artifact.Pages.Single();
        var layerId = page.Layers.Single().LayerId;
        using var sourceExtension = JsonDocument.Parse("\"source\"");
        using var mutatedExtension = JsonDocument.Parse("\"mutated by reader\"");
        var stroke = new CanvasInkStroke
        {
            LayerId = layerId,
            Samples = [new(1, 1), new(2, 2)],
            PathGeometry = new CanvasStrokePathGeometry
            {
                Start = new CanvasStrokePathElement(1, 1, .5),
                Segments = [new CanvasStrokePathSegment
                {
                    Kind = CanvasStrokePathSegmentKind.Line,
                    End = new CanvasStrokePathElement(2, 2, .7)
                }]
            },
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                { ["donorProvenance"] = sourceExtension.RootElement.Clone() }
        };
        var ordinaryStroke = new CanvasInkStroke
        {
            LayerId = layerId, Samples = [new(10, 10), new(11, 11)]
        };
        artifact.Pages[0] = page with
        {
            Strokes = [stroke, ordinaryStroke],
            StrokeOrder = [stroke.StrokeId, ordinaryStroke.StrokeId]
        };
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var before = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));

        var read = session.GetStroke(page.PageId, stroke.StrokeId);
        Assert.True(read.IsSuccess);
        var detached = read.Value!;
        Assert.Equal(stroke.StrokeId, detached.StrokeId);
        Assert.Single(detached.PathGeometry!.Segments);
        Assert.Equal("source", detached.ExtensionData!["donorProvenance"].GetString());
        detached.Samples.Clear();
        detached.PathGeometry.Segments.Clear();
        detached.ExtensionData["donorProvenance"] = mutatedExtension.RootElement.Clone();

        var reread = session.GetStroke(page.PageId, stroke.StrokeId);
        Assert.True(reread.IsSuccess);
        Assert.Equal(2, reread.Value!.Samples.Count);
        Assert.Single(reread.Value.PathGeometry!.Segments);
        Assert.Equal("source", reread.Value.ExtensionData!["donorProvenance"].GetString());
        Assert.Equal(CanvasApiErrorCode.NotFound, session.GetStroke(page.PageId, Guid.NewGuid()).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound, session.GetStroke(Guid.NewGuid(), stroke.StrokeId).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Equal(before, Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);

        var deletedOrdinaryStroke = session.DeleteStructuredStroke(
            new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")),
            page.PageId, ordinaryStroke.StrokeId);
        Assert.True(deletedOrdinaryStroke.IsSuccess);
        Assert.Equal(stroke.StrokeId, Assert.Single(session.GetPage(page.PageId).Value!.Strokes).StrokeId);
    }

    [Fact]
    public void Listing_layers_is_bounded_page_scoped_and_follows_canonical_order()
    {
        var artifact = CanvasArtifact.Create("Layer listing");
        var page = artifact.Pages.Single();
        var first = page.Layers.Single();
        var hidden = new CanvasLayer { Name = "Hidden", IsVisible = false };
        var locked = new CanvasLayer { Name = "Locked", IsLocked = true };
        artifact.Pages[0] = page with
        {
            Layers = [first, hidden, locked],
            LayerOrder = [hidden.LayerId, first.LayerId, locked.LayerId]
        };
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;

        var firstPage = session.ListLayers(page.PageId, offset: 1, pageSize: 1);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value!.TotalCount);
        Assert.Equal(2, firstPage.Value.NextOffset);
        var listedFirst = Assert.Single(firstPage.Value.Items);
        Assert.Equal(first.LayerId, listedFirst.LayerId);
        Assert.True(listedFirst.IsVisible);

        var finalPage = session.ListLayers(page.PageId, offset: 2, pageSize: 1);
        Assert.True(finalPage.IsSuccess);
        Assert.Null(finalPage.Value!.NextOffset);
        var listedLocked = Assert.Single(finalPage.Value.Items);
        Assert.Equal(locked.LayerId, listedLocked.LayerId);
        Assert.True(listedLocked.IsLocked);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.ListLayers(page.PageId, pageSize: 0).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.NotFound,
            session.ListLayers(Guid.NewGuid()).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Setting_page_background_detaches_extension_data_and_identical_update_is_a_no_op()
    {
        var artifact = CanvasArtifact.Create("Detached background");
        var page = artifact.Pages.Single();
        using var originalExtension = JsonDocument.Parse("\"initial\"");
        using var changedExtension = JsonDocument.Parse("\"mutated externally\"");
        var externalExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["ownerExtension"] = originalExtension.RootElement.Clone()
        };
        var background = new CanvasBackgroundDefinition
        {
            Kind = "solid", Color = "#FF123456", ExtensionData = externalExtensionData
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        var set = session.SetPageBackground(Request(), page.PageId, background);
        Assert.True(set.IsSuccess);
        var committedRevision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var committedSnapshot = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));

        externalExtensionData["ownerExtension"] = changedExtension.RootElement.Clone();

        Assert.Equal(committedRevision, session.CurrentRevisionId);
        Assert.Equal(committedSnapshot,
            Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        var storedBackground = session.GetPage(page.PageId).Value!.Background;
        Assert.Equal("initial", storedBackground.ExtensionData!["ownerExtension"].GetString());

        var repeated = session.SetPageBackground(Request(), page.PageId, storedBackground);
        Assert.True(repeated.IsSuccess);
        Assert.Empty(repeated.Value!.ChangedIds);
        Assert.Equal(committedRevision, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Moving_object_to_its_current_placement_is_a_canonical_no_op()
    {
        var artifact = CanvasArtifact.Create("Unchanged placement");
        var page = artifact.Pages.Single();
        using var payload = JsonDocument.Parse("{}");
        var sharedObject = new CanvasObject
        {
            LayerId = page.Layers.Single().LayerId,
            ObjectTypeId = "test.shared/v1",
            SharedPayload = payload.RootElement.Clone()
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));
        Assert.True(session.InsertSharedObjects(Request(), page.PageId, [sharedObject]).IsSuccess);
        var current = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        var revision = session.CurrentRevisionId;
        var cursor = session.GetChanges().Value!.NextCursor;
        var before = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));

        var result = session.MoveObject(Request(), page.PageId, current.ObjectId,
            current.Geometry, current.Transform);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.ChangedIds);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Equal(before, Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Empty(session.GetChanges(cursor).Value!.Events);
    }

    [Fact]
    public void Setting_identical_page_bounds_is_a_canonical_no_op()
    {
        var artifact = CanvasArtifact.Create("Page bounds", CanvasDocumentMode.Paged);
        var page = artifact.Pages.Single();
        var bounds = page.Bounds!;
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var before = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        var unchanged = session.SetPageBounds(Request(), page.PageId, bounds);
        Assert.True(unchanged.IsSuccess);
        Assert.Empty(unchanged.Value!.ChangedIds);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Equal(before, Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.SetPageBounds(Request(), page.PageId, bounds with { Width = 0 }).Error!.Code);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Updating_identical_document_settings_does_not_create_history_or_events()
    {
        var artifact = CanvasArtifact.Create("Document settings no-op");
        var session = new CanvasArtifactSession(artifact);
        var revision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var before = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        var unchanged = session.UpdateDocumentSettings(Request(), new CanvasDocumentSettings());

        Assert.True(unchanged.IsSuccess);
        Assert.Empty(unchanged.Value!.ChangedIds);
        Assert.Equal(revision, session.CurrentRevisionId);
        Assert.Equal(before, Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);

        using var property = JsonDocument.Parse("true");
        var changed = session.UpdateDocumentSettings(Request(), new CanvasDocumentSettings
        {
            Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                { ["showGrid"] = property.RootElement.Clone() }
        });
        Assert.True(changed.IsSuccess);
        Assert.Contains(session.ArtifactId, changed.Value!.ChangedIds);
        Assert.NotEqual(revision, session.CurrentRevisionId);
    }

    [Fact]
    public void Deleting_primary_multi_selection_promotes_remaining_canonical_selection()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Selection history"));
        var controller = new CanvasInteractionController(board);
        var first = controller.AddObject(NotesCanvasObjectKind.Text, "First");
        var second = controller.AddObject(NotesCanvasObjectKind.Shape, "Second");
        controller.SetSelection([first.Id, second.Id]);
        var deletedId = controller.SelectedObjectId!.Value;
        var remainingId = deletedId == first.Id ? second.Id : first.Id;

        Assert.True(controller.DeleteSelected());

        Assert.Equal(remainingId, controller.SelectedObjectId);
        Assert.Equal(remainingId, controller.SelectedObject!.Id);
        Assert.Equal(new[] { remainingId }, controller.SelectedObjectIds);
        Assert.DoesNotContain(deletedId, controller.SelectedObjectIds);
        Assert.True(controller.Undo());
        Assert.Equal(remainingId, controller.SelectedObjectId);
        Assert.Equal(remainingId, controller.SelectedObject!.Id);

        controller.SelectObject(deletedId);
        Assert.True(controller.DeleteSelected());
        Assert.Null(controller.SelectedObjectId);
        Assert.Empty(controller.SelectedObjectIds);
    }

    [Fact]
    public void Replacing_document_reconciles_selection_to_surviving_object_ids()
    {
        var controller = new CanvasInteractionController(
            CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Selection replacement")));
        var first = controller.AddObject(NotesCanvasObjectKind.Text, "First");
        var second = controller.AddObject(NotesCanvasObjectKind.Shape, "Second");
        controller.SetSelection([first.Id, second.Id]);
        var removedId = controller.SelectedObjectId!.Value;
        var survivorId = removedId == first.Id ? second.Id : first.Id;
        var replacement = CanvasDocumentModel.CloneBoard(controller.Board);
        replacement.Objects.RemoveAll(value => value.Id == removedId);

        controller.ReplaceBoard(replacement);

        Assert.Equal(survivorId, controller.SelectedObjectId);
        Assert.Equal(new[] { survivorId }, controller.SelectedObjectIds);
        Assert.DoesNotContain(removedId, controller.SelectedObjectIds);
    }

    [Fact]
    public void Unreferenced_shared_object_payload_remains_editable_with_stable_identity()
    {
        var artifact = CanvasArtifact.Create("Canvas-owned shared object");
        var session = new CanvasArtifactSession(artifact);
        var page = artifact.Pages.Single();
        using var initialPayload = JsonDocument.Parse("{\"text\":\"before\"}");
        var sharedObject = new CanvasObject
        {
            LayerId = page.Layers.Single().LayerId,
            ObjectTypeId = "test.shared.RichText/v1",
            SharedPayload = initialPayload.RootElement.Clone()
        };
        var inserted = session.InsertSharedObjects(
            new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")),
            page.PageId, [sharedObject]);
        Assert.True(inserted.IsSuccess);

        using var editedPayload = JsonDocument.Parse("{\"text\":\"after\"}");
        var updated = session.UpdateSharedObject(
            new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test")),
            page.PageId, sharedObject with { SharedPayload = editedPayload.RootElement.Clone() });

        Assert.True(updated.IsSuccess);
        var saved = Assert.Single(session.GetArtifactSnapshot().Pages.Single().Objects);
        Assert.Equal(sharedObject.ObjectId, saved.ObjectId);
        Assert.Equal("after", saved.SharedPayload!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public void Hidden_layers_deny_edits_without_blocking_unrelated_visible_strokes()
    {
        var artifact = CanvasArtifact.Create("Layer visibility");
        var page = artifact.Pages.Single();
        var visibleLayer = page.Layers.Single();
        var hiddenLayer = new CanvasLayer { Name = "Hidden" };
        using var payload = JsonDocument.Parse("{}");
        var hiddenObject = new CanvasObject
        {
            LayerId = hiddenLayer.LayerId, ObjectTypeId = "test.shared/v1",
            SharedPayload = payload.RootElement.Clone()
        };
        var visibleObject = new CanvasObject
        {
            LayerId = visibleLayer.LayerId, ObjectTypeId = "test.shared/v1",
            SharedPayload = payload.RootElement.Clone()
        };
        var hiddenStroke = new CanvasInkStroke
        {
            LayerId = hiddenLayer.LayerId, Samples = [new(1, 1), new(2, 2)]
        };
        var visibleStroke = new CanvasInkStroke
        {
            LayerId = visibleLayer.LayerId, Samples = [new(3, 3), new(4, 4)]
        };
        artifact.Pages[0] = page with
        {
            Layers = [visibleLayer, hiddenLayer],
            LayerOrder = [visibleLayer.LayerId, hiddenLayer.LayerId],
            Objects = [hiddenObject, visibleObject],
            ObjectOrder = [hiddenObject.ObjectId, visibleObject.ObjectId],
            Strokes = [hiddenStroke, visibleStroke],
            StrokeOrder = [hiddenStroke.StrokeId, visibleStroke.StrokeId]
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));

        Assert.True(session.SetLayerVisibility(Request(), page.PageId, hiddenLayer.LayerId, false).IsSuccess);
        var revisionAfterHide = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;

        var moveHiddenObject = session.MoveObject(Request(), page.PageId, hiddenObject.ObjectId,
            hiddenObject.Geometry with { X = 10 });
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, moveHiddenObject.Error!.Code);
        var updateHiddenObject = session.UpdateSharedObject(Request(), page.PageId,
            hiddenObject with { Geometry = hiddenObject.Geometry with { X = 20 } });
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, updateHiddenObject.Error!.Code);
        var deleteHiddenObject = session.DeleteObject(Request(), page.PageId, hiddenObject.ObjectId);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, deleteHiddenObject.Error!.Code);
        var moveOutOfHiddenLayer = session.MoveObjectToLayer(Request(), page.PageId,
            hiddenObject.ObjectId, visibleLayer.LayerId);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, moveOutOfHiddenLayer.Error!.Code);
        var insertHiddenObject = session.InsertSharedObjects(Request(), page.PageId,
            [new CanvasObject { LayerId = hiddenLayer.LayerId, ObjectTypeId = "test.shared/v1", SharedPayload = payload.RootElement.Clone() }]);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, insertHiddenObject.Error!.Code);
        var moveIntoHiddenLayer = session.MoveObjectToLayer(Request(), page.PageId,
            visibleObject.ObjectId, hiddenLayer.LayerId);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, moveIntoHiddenLayer.Error!.Code);
        var addHiddenStroke = session.AddStructuredStroke(Request(), page.PageId,
            new CanvasInkStroke { LayerId = hiddenLayer.LayerId, Samples = [new(5, 5), new(6, 6)] });
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, addHiddenStroke.Error!.Code);
        var deleteHiddenStroke = session.DeleteStructuredStroke(Request(), page.PageId, hiddenStroke.StrokeId);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, deleteHiddenStroke.Error!.Code);
        var deleteHiddenBatch = session.DeleteStructuredStrokes(Request(), page.PageId, [hiddenStroke.StrokeId]);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, deleteHiddenBatch.Error!.Code);
        var replaceHiddenStroke = session.ReplaceStructuredStrokes(Request(), page.PageId,
            [hiddenStroke with { RevisionId = Guid.NewGuid(), Samples = [new(7, 7), new(8, 8)] }, visibleStroke],
            [], [hiddenStroke.StrokeId, visibleStroke.StrokeId]);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, replaceHiddenStroke.Error!.Code);

        Assert.Equal(revisionAfterHide, session.CurrentRevisionId);
        var deniedEvents = session.GetChanges(eventCursor);
        Assert.True(deniedEvents.IsSuccess);
        Assert.Empty(deniedEvents.Value!.Events);

        var changedVisibleStroke = visibleStroke with
        {
            RevisionId = Guid.NewGuid(),
            Samples = [new(30, 3), new(4, 4)]
        };
        var visibleEdit = session.ReplaceStructuredStrokes(Request(), page.PageId,
            [hiddenStroke, changedVisibleStroke], [], [hiddenStroke.StrokeId, visibleStroke.StrokeId]);
        Assert.True(visibleEdit.IsSuccess);
        var visibleEditResult = visibleEdit.Value!;
        Assert.Contains(visibleStroke.StrokeId, visibleEditResult.ChangedIds);
        Assert.DoesNotContain(hiddenStroke.StrokeId, visibleEditResult.ChangedIds);
        var savedPage = session.GetPage(page.PageId).Value!;
        Assert.Contains(savedPage.Strokes, stroke => stroke.StrokeId == hiddenStroke.StrokeId);
        Assert.Equal(30, savedPage.Strokes.Single(stroke => stroke.StrokeId == visibleStroke.StrokeId).Samples[0].X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_stroke_order_cannot_reorder_hidden_or_locked_strokes(bool lockLayer)
    {
        var artifact = CanvasArtifact.Create("Protected stroke order");
        var page = artifact.Pages.Single();
        var layer = page.Layers.Single();
        var first = new CanvasInkStroke { LayerId = layer.LayerId, Samples = [new(1, 1), new(2, 2)] };
        var second = new CanvasInkStroke { LayerId = layer.LayerId, Samples = [new(3, 3), new(4, 4)] };
        artifact.Pages[0] = page with
        {
            Strokes = [first, second],
            StrokeOrder = [first.StrokeId, second.StrokeId]
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));
        var protectedLayer = lockLayer
            ? session.SetLayerLocked(Request(), page.PageId, layer.LayerId, true)
            : session.SetLayerVisibility(Request(), page.PageId, layer.LayerId, false);
        Assert.True(protectedLayer.IsSuccess);

        var baseRevision = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var beforeDeniedOperation = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var operation = Request();
        var donorSettingsCalls = 0;
        CanvasDocumentSettings CaptureSettings()
        {
            donorSettingsCalls++;
            return new CanvasDocumentSettings();
        }

        CanvasApiResult<CanvasMutationResult> AttemptReorder() => session.ReplaceStructuredStrokes(
            operation, page.PageId, Array.Empty<CanvasInkStroke>(), Array.Empty<Guid>(),
            [second.StrokeId, first.StrokeId], CaptureSettings);

        var denied = AttemptReorder();
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, denied.Error!.Code);
        var retried = AttemptReorder();
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, retried.Error!.Code);
        Assert.Equal(0, donorSettingsCalls);
        Assert.Equal(baseRevision, session.CurrentRevisionId);
        Assert.Equal(beforeDeniedOperation,
            Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        var events = session.GetChanges(eventCursor);
        Assert.True(events.IsSuccess);
        Assert.Empty(events.Value!.Events);
        Assert.Equal(new[] { first.StrokeId, second.StrokeId },
            session.GetArtifactSnapshot().Pages.Single().StrokeOrder);
    }

    [Fact]
    public void Protected_stroke_order_guard_handles_many_mixed_layers_and_unrelated_transaction_edits()
    {
        const int layerCount = 32;
        var artifact = CanvasArtifact.Create("Mixed protected stroke order");
        var page = artifact.Pages.Single();
        var layers = Enumerable.Range(0, layerCount)
            .Select(index => new CanvasLayer
            {
                Name = $"Layer {index + 1}",
                IsVisible = index != 0,
                IsLocked = index == 1
            }).ToArray();
        var strokes = layers.Select((layer, index) => new CanvasInkStroke
        {
            LayerId = layer.LayerId,
            Samples = [new(index * 10, 1), new(index * 10 + 1, 2)]
        }).ToArray();
        artifact.Pages[0] = page with
        {
            Layers = layers.ToList(),
            LayerOrder = layers.Select(layer => layer.LayerId).ToList(),
            Strokes = strokes.ToList(),
            StrokeOrder = strokes.Select(stroke => stroke.StrokeId).ToList()
        };
        var session = new CanvasArtifactSession(artifact);
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), new("canvas-test", "Canvas test"));
        var changedVisibleStroke = strokes[2] with
        {
            RevisionId = Guid.NewGuid(),
            Samples = [new(500, 1), new(501, 2)]
        };
        var addedVisibleStroke = new CanvasInkStroke
        {
            LayerId = layers[^1].LayerId,
            Samples = [new(600, 1), new(601, 2)]
        };
        var removedVisibleStroke = strokes[^1];
        var safeOrder = strokes.Where(stroke => stroke.StrokeId != removedVisibleStroke.StrokeId)
            .Select(stroke => stroke.StrokeId).ToList();
        (safeOrder[2], safeOrder[3]) = (safeOrder[3], safeOrder[2]);
        safeOrder.Add(addedVisibleStroke.StrokeId);

        var unrelatedEdit = session.ReplaceStructuredStrokes(Request(), page.PageId,
            [changedVisibleStroke, addedVisibleStroke], [removedVisibleStroke.StrokeId], safeOrder);
        Assert.True(unrelatedEdit.IsSuccess);
        var safelyEditedPage = session.GetPage(page.PageId).Value!;
        Assert.Equal(strokes[0].StrokeId, safelyEditedPage.StrokeOrder[0]);
        Assert.Equal(strokes[1].StrokeId, safelyEditedPage.StrokeOrder[1]);
        Assert.Equal(safeOrder, safelyEditedPage.StrokeOrder);

        var revisionBeforeDeniedCrossing = session.CurrentRevisionId;
        var eventCursor = session.GetChanges().Value!.NextCursor;
        var beforeDeniedCrossing = Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        var crossingOrder = safelyEditedPage.StrokeOrder.ToList();
        var hiddenStrokeId = strokes[0].StrokeId;
        crossingOrder.Remove(hiddenStrokeId);
        crossingOrder.Insert(3, hiddenStrokeId);
        var deniedCrossing = session.ReplaceStructuredStrokes(Request(), page.PageId,
            [], [], crossingOrder);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied, deniedCrossing.Error!.Code);
        Assert.Equal(revisionBeforeDeniedCrossing, session.CurrentRevisionId);
        Assert.Equal(beforeDeniedCrossing,
            Convert.ToHexString(CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())));
        Assert.Empty(session.GetChanges(eventCursor).Value!.Events);
    }

    [Fact]
    public void Controller_supports_object_gestures_history_connections_grouping_and_locks()
    {
        var document = CanvasDocumentModel.Create("Architecture board");
        var board = CanvasDocumentModel.GetBoard(document);
        board.OffsetX = 0;
        board.OffsetY = 0;
        board.Zoom = 1;
        var controller = new CanvasInteractionController(board);

        var first = controller.AddObject(NotesCanvasObjectKind.Text, "Alpha");
        Assert.True(controller.MoveSelected(120, 140));
        Assert.True(controller.ResizeSelected(240, 130));
        Assert.True(controller.RotateSelected(25));
        var second = controller.AddObject(NotesCanvasObjectKind.Shape, "Beta");
        Assert.Equal(second.Id, controller.SelectedObjectId);
        Assert.Equal(new[] { second.Id }, controller.SelectedObjectIds);
        Assert.True(controller.MoveSelected(520, 420));
        var connector = controller.Connect(first.Id, second.Id, "depends on");
        Assert.NotNull(connector);
        Assert.Equal(first.Id, connector!.FromObjectId);
        Assert.Equal(second.Id, connector.ToObjectId);
        Assert.Equal(connector.Id, controller.SelectedObjectId);
        Assert.Equal(new[] { connector.Id }, controller.SelectedObjectIds);
        Assert.NotNull(controller.Group(first.Id, second.Id));
        Assert.Equal(second.Id, controller.SelectedObjectId);
        Assert.Equal(new[] { second.Id }, controller.SelectedObjectIds);

        controller.SelectObject(first.Id);
        var beforeDragX = controller.SelectedObject!.X;
        var beforeDragY = controller.SelectedObject.Y;
        var dragStartX = beforeDragX + controller.SelectedObject.Width / 2;
        var dragStartY = beforeDragY + controller.SelectedObject.Height / 2;
        controller.Tool = CanvasTool.Select;
        controller.Begin(new CanvasPointerSample(dragStartX, dragStartY));
        Assert.True(controller.Move(new CanvasPointerSample(dragStartX + 60, dragStartY + 40)));
        controller.End(new CanvasPointerSample(dragStartX + 60, dragStartY + 40));
        Assert.True(controller.Board.Objects.Single(value => value.Id == first.Id).X > beforeDragX);
        Assert.True(controller.Undo());
        Assert.Equal(beforeDragX, controller.Board.Objects.Single(value => value.Id == first.Id).X, 3);
        Assert.True(controller.Redo());
        Assert.True(controller.Board.Objects.Single(value => value.Id == first.Id).X > beforeDragX);

        controller.SelectObject(second.Id);
        Assert.True(controller.SetSelectedLocked(true));
        var locked = controller.SelectedObject!;
        Assert.False(controller.MoveSelected(locked.X + 50, locked.Y + 50));
        Assert.False(controller.ResizeSelected(locked.Width + 50, locked.Height + 50));
        Assert.False(controller.RotateSelected(locked.Rotation + 30));
        Assert.False(controller.UpdateSelectedText("Blocked"));
        Assert.False(controller.SendSelectedToBack());
        Assert.False(controller.DeleteSelected());

        controller.SelectObject(first.Id);
        Assert.False(controller.UngroupSelected());
    }

    [Fact]
    public void Ink_preserves_pressure_tilt_supports_undo_redo_and_eraser()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Ink board"));
        board.OffsetX = 0;
        board.OffsetY = 0;
        board.Zoom = 1;
        var controller = new CanvasInteractionController(board) { Tool = CanvasTool.Pen, PenWidth = 5, PenColour = "#FF123456" };

        Assert.True(controller.Begin(new CanvasPointerSample(10, 20, 0.8, 12, -9, 1_000)));
        Assert.True(controller.Move(new CanvasPointerSample(25, 35, 0.6, 8, -4, 1_020)));
        controller.End(new CanvasPointerSample(40, 50, 0.4, 4, -2, 1_040));

        var stroke = Assert.Single(controller.Board.Strokes);
        Assert.Equal("pen", stroke.Tool);
        Assert.Equal("#FF123456", stroke.Colour);
        Assert.Equal(5, stroke.BaseWidth, 3);
        Assert.True(stroke.Points.Count >= 3);
        Assert.Equal(0.8, stroke.Points[0].Pressure, 3);
        Assert.Equal(12, stroke.Points[0].TiltX, 3);
        Assert.Equal(-9, stroke.Points[0].TiltY, 3);
        Assert.Equal(20, stroke.Points[1].TimestampMilliseconds);

        Assert.True(controller.Undo());
        Assert.Empty(controller.Board.Strokes);
        Assert.True(controller.Redo());
        Assert.Single(controller.Board.Strokes);

        controller.Tool = CanvasTool.Eraser;
        Assert.True(controller.Begin(new CanvasPointerSample(10, 20)));
        Assert.Empty(controller.Board.Strokes);
        Assert.True(controller.Undo());
        Assert.Single(controller.Board.Strokes);
    }

    [Fact]
    public async Task Canvas_native_format_and_notes_repository_round_trip_and_recover_last_valid_board()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(_paths, validator, diagnostics);
        var formats = new NotesImportExportService(validator, diagnostics);
        var document = CanvasDocumentModel.Create("Persisted canvas");
        var controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document));
        controller.AddObject(NotesCanvasObjectKind.Text, "Persist me");
        controller.Tool = CanvasTool.Highlighter;
        controller.Begin(new CanvasPointerSample(30, 40, 0.7, 5, 6, 500));
        controller.End(new CanvasPointerSample(80, 90, 0.5, 2, 3, 530));
        CanvasDocumentModel.ReplaceBoard(document, controller.Board);

        await repository.SaveAsync(document, "Canvas persistence test", CancellationToken.None);
        var loaded = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(loaded));
        var loadedBoard = CanvasDocumentModel.GetBoard(loaded!);
        Assert.Single(loadedBoard.Objects);
        Assert.Single(loadedBoard.Strokes);
        Assert.Equal("Persist me", loadedBoard.Objects[0].Text);

        var nativePath = Path.Combine(_paths.DataDirectory, "persisted-canvas.haven-notes.json");
        await formats.ExportAsync(loaded, nativePath, CancellationToken.None);
        var imported = await formats.ImportAsync(nativePath, CancellationToken.None);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(imported));
        Assert.Equal("Persist me", CanvasDocumentModel.GetBoard(imported).Objects.Single().Text);

        var secondController = new CanvasInteractionController(loadedBoard);
        secondController.AddObject(NotesCanvasObjectKind.Frame, "Transient second version");
        CanvasDocumentModel.ReplaceBoard(loaded, secondController.Board);
        await repository.SaveAsync(loaded, "Second Canvas version", CancellationToken.None);

        var currentPath = Path.Combine(_paths.DataDirectory, "Notes", "Documents", document.Id.ToString("D"), "current.haven-notes.json");
        await File.WriteAllTextAsync(currentPath, "{ this is not valid json");
        var recovered = await repository.LoadAsync(document.Id, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.True(recovered!.Recovery.HasUnsavedRecovery);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(recovered));
        var recoveredBoard = CanvasDocumentModel.GetBoard(recovered);
        Assert.Single(recoveredBoard.Objects);
        Assert.Equal("Persist me", recoveredBoard.Objects[0].Text);
    }

    [Fact]
    public void Ghost_visibility_hides_unrevealed_layer_content_without_mutating_the_board()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Study board"));
        var visibleObject = new NotesCanvasObject { Kind = NotesCanvasObjectKind.Text, Text = "Question" };
        var answerObject = new NotesCanvasObject { Kind = NotesCanvasObjectKind.Text, Text = "Answer" };
        var answerStroke = new NotesInkStroke
        {
            IsGhost = true,
            Points = [new NotesInkPoint { X = 1, Y = 1 }, new NotesInkPoint { X = 2, Y = 2 }]
        };
        var layer = new NotesGhostLayer
        {
            Name = "Answer",
            IsRevealed = false,
            ObjectIds = [answerObject.Id],
            StrokeIds = [answerStroke.Id]
        };
        answerStroke.GhostLayerId = layer.Id;
        board.Objects.Add(visibleObject);
        board.Objects.Add(answerObject);
        board.Strokes.Add(answerStroke);
        board.GhostLayers.Add(layer);

        Assert.True(CanvasGhostVisibility.IsObjectVisible(board, visibleObject));
        Assert.False(CanvasGhostVisibility.IsObjectVisible(board, answerObject));
        Assert.False(CanvasGhostVisibility.IsStrokeVisible(board, answerStroke));
        Assert.Equal(2, board.Objects.Count);
        Assert.Single(board.Strokes);

        layer.IsRevealed = true;
        Assert.True(CanvasGhostVisibility.IsObjectVisible(board, answerObject));
        Assert.True(CanvasGhostVisibility.IsStrokeVisible(board, answerStroke));
        Assert.Equal(2, board.Objects.Count);
        Assert.Single(board.Strokes);
    }

    [Fact]
    public void Diagonal_line_layout_is_centered_before_rotation()
    {
        var layout = CanvasLineGeometry.Compute(10, 20, 110, 120, 4);
        Assert.Equal(Math.Sqrt(20_000), layout.Width, 6);
        Assert.Equal(4, layout.Height, 6);
        Assert.Equal(45, layout.RotationDegrees, 6);
        Assert.Equal(60 - Math.Sqrt(20_000) / 2, layout.Left, 6);
        Assert.Equal(68, layout.Top, 6);
    }

    [Fact]
    public void Multi_selection_supports_marquee_atomic_transform_clipboard_and_undo()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Direct manipulation"));
        board.OffsetX = 0; board.OffsetY = 0; board.Zoom = 1;
        var controller = new CanvasInteractionController(board);
        var first = controller.AddObjectAt(NotesCanvasObjectKind.Shape, 100, 100, 120, 80, "One");
        var second = controller.AddObjectAt(NotesCanvasObjectKind.Text, 280, 120, 140, 70, "Two");
        _ = controller.AddObjectAt(NotesCanvasObjectKind.Frame, 700, 500, 180, 120, "Outside");

        var selected = controller.SelectViewportRectangle(80, 80, 380, 160);
        Assert.Equal(2, selected.Count);
        Assert.Contains(first.Id, selected);
        Assert.Contains(second.Id, selected);

        var firstX = first.X; var secondX = second.X; var firstWidth = first.Width;
        Assert.True(controller.TransformSelection(25, 15, 60, 30, 20));
        Assert.True(first.X > firstX);
        Assert.True(second.X > secondX);
        Assert.True(first.Width > firstWidth);
        Assert.Equal(20, first.Rotation, 3);
        Assert.Equal(20, second.Rotation, 3);

        Assert.True(controller.Undo());
        var restoredFirst = controller.Board.Objects.Single(value => value.Id == first.Id);
        var restoredSecond = controller.Board.Objects.Single(value => value.Id == second.Id);
        Assert.Equal(firstX, restoredFirst.X, 3);
        Assert.Equal(secondX, restoredSecond.X, 3);
        Assert.Equal(firstWidth, restoredFirst.Width, 3);

        controller.SetSelection([restoredFirst.Id, restoredSecond.Id]);
        Assert.True(controller.CopySelection());
        Assert.True(controller.PasteSelection());
        Assert.Equal(2, controller.SelectedObjectIds.Count);
        Assert.Equal(5, controller.Board.Objects.Count);
        Assert.DoesNotContain(controller.SelectedObjectIds, id => id == restoredFirst.Id || id == restoredSecond.Id);
    }

    [Fact]
    public void Rotated_hit_testing_matches_visible_geometry()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Rotated geometry"));
        board.OffsetX = 0; board.OffsetY = 0; board.Zoom = 1;
        var controller = new CanvasInteractionController(board);
        var value = controller.AddObjectAt(NotesCanvasObjectKind.Shape, 100, 100, 200, 40, "Rotated");
        controller.SelectObject(value.Id);
        Assert.True(controller.RotateSelected(90));

        Assert.Equal(value.Id, controller.HitObjectAtViewport(200, 30)?.Id);
        Assert.Null(controller.HitObjectAtViewport(110, 110));
    }

    [Fact]
    public void Hundreds_of_canvas_objects_transform_without_rebuilding_the_model()
    {
        var board = CanvasDocumentModel.GetBoard(CanvasDocumentModel.Create("Stress"));
        board.OffsetX = 0; board.OffsetY = 0; board.Zoom = 1;
        var controller = new CanvasInteractionController(board);
        for (var index = 0; index < 400; index++)
            controller.AddObjectAt(NotesCanvasObjectKind.Shape, (index % 20) * 60, (index / 20) * 50, 40, 30, index.ToString());
        var ids = controller.Board.Objects.Select(value => value.Id).ToArray();
        controller.SetSelection(ids);

        Assert.True(controller.TranslateSelection(5, 7, snap: false));
        Assert.Equal(400, controller.Board.Objects.Count);
        Assert.All(controller.Board.Objects, value => Assert.True(value.X >= 5 && value.Y >= 7));
        Assert.True(controller.Undo());
        Assert.Equal(400, controller.Board.Objects.Count);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-canvas-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

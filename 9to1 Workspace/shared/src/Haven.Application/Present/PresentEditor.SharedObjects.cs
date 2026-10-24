using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>App-neutral transport identity, validated by the actual Home object owner.</summary>
public sealed record PresentSharedObjectCapture(Guid ObjectId, string ObjectType, int SchemaVersion, JsonElement Bundle);
public sealed record PresentSharedObjectSupport(bool Compatible, bool CanRender, string Code, string Message);
public sealed record PresentSharedObjectInsertionResult(bool Inserted, string Code, string Message, Guid? ElementId = null);
public sealed record PresentSharedObjectUpdateResult(bool Changed, string Code, string Message, Guid? ElementId = null);
public sealed record PresentSharedObjectEraseResult(bool Changed, string Code, string Message, IReadOnlyList<Guid> ChangedElementIds);

/// <summary>No app-private schema or ink renderer: implementations delegate to Home's real registry.</summary>
public interface IPresentSharedObjectBridge
{
    PresentSharedObjectCapture Capture(JsonElement canonicalBundle);
    PresentSharedObjectCapture CloneForPaste(JsonElement canonicalBundle);
    PresentSharedObjectSupport GetSupport(JsonElement canonicalBundle);
}

public sealed class PresentSharedObjectCapabilityException(string code, string message) : NotSupportedException(message)
{
    public string Code { get; } = code;
}

public sealed partial class PresentEditor
{
    /// <summary>
    /// One normal undoable insertion, only after the actual object and surface capabilities agree.
    /// This prepares document state; only the existing owning save transaction can claim durability.
    /// </summary>
    public PresentSharedObjectInsertionResult TryInsertSharedObject(Guid slideId, JsonElement canonicalBundle,
        IPresentSharedObjectBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _ = RequireSlide(slideId);
        var support = bridge.GetSupport(canonicalBundle);
        if (!support.Compatible || !support.CanRender)
            return new(false, support.Code, support.Message);
        // Home's canonical clone handler remints inner identities before any document/history mutation.
        var captured = bridge.CloneForPaste(canonicalBundle);
        if (captured.ObjectId == Guid.Empty || string.IsNullOrWhiteSpace(captured.ObjectType) ||
            captured.SchemaVersion < 1 || captured.Bundle.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The shared object owner returned an invalid canonical capture.");
        if (Document.Slides.SelectMany(slide => slide.Elements).Any(element => element.Id == captured.ObjectId))
            return new(false, "ObjectAlreadyExists", "The canonical shared object identity already belongs to this presentation.");
        var element = AddElement(slideId, new PresentElement
        {
            Id = captured.ObjectId,
            Kind = PresentElementKind.SharedObject,
            SharedProductivityBundle = captured.Bundle.Clone(),
            X = .18, Y = .18, Width = .40, Height = .40
        });
        return new(true, "PreparedInDocument", "The shared object is in the undoable document; save is still required.", element.Id);
    }

    /// <summary>Insert an owner-validated fresh shared stroke, with precise placement in ONE existing
    /// undo transaction. Native rendering/readiness and persistence remain the owning surface's jobs.</summary>
    public PresentSharedObjectInsertionResult TryInsertPreparedSharedObject(Guid expectedDocumentId, int expectedDocumentVersion,
        Guid selectedSlideId, JsonElement canonicalBundle, IPresentSharedObjectBridge bridge,
        double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        var document = Document;
        if (IsLiveTextEditActive) return new(false, "TextEditInProgress", "Finish the current text edit before drawing.");
        if (document.Id != expectedDocumentId || document.Version != expectedDocumentVersion || Selection.SlideId != selectedSlideId)
            return new(false, "RevisionConflict", "The presentation or selected slide changed before stroke insertion.");
        var slide = document.Slides.SingleOrDefault(value => value.Id == selectedSlideId);
        if (slide is null) return new(false, "SlideNotFound", "The original slide no longer exists.");
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            x is < -1 or > 1 || y is < -1 or > 1 || width is < .000001 or > 2 || height is < .000001 or > 2)
            return new(false, "InvalidGeometry", "The actual shared stroke bounds cannot be placed on this slide.");
        PresentSharedObjectCapture captured;
        try { captured = bridge.Capture(canonicalBundle); }
        catch (PresentSharedObjectCapabilityException error) { return new(false, error.Code, error.Message); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(false, "InvalidOrUnsupportedObject", error.Message); }
        if (captured.ObjectId == Guid.Empty || captured.ObjectType != "drawing.ink" || captured.SchemaVersion != 1 ||
            document.Slides.SelectMany(value => value.Elements).Any(value => value.Id == captured.ObjectId))
            return new(false, "ObjectIdentityMismatch", "The fresh shared stroke identity is invalid or already belongs to this presentation.");
        if (IsLiveTextEditActive || !ReferenceEquals(Document, document) || document.Version != expectedDocumentVersion ||
            Selection.SlideId != selectedSlideId || !document.Slides.Any(value => ReferenceEquals(value, slide)))
            return new(false, "RevisionConflict", "The original slide changed while the shared stroke was prepared.");
        var inserted = AddElement(selectedSlideId, new PresentElement
        {
            Id = captured.ObjectId, Kind = PresentElementKind.SharedObject, SharedProductivityBundle = captured.Bundle.Clone(),
            AlternativeText = "Drawing stroke", X = x, Y = y, Width = width, Height = height
        });
        return new(true, "PreparedInDocument", "The stroke is in the existing undo history; save is still required.", inserted.Id);
    }

    /// <summary>Apply an owner-prepared shared change to the exact current payload, in the existing undo history.
    /// The owning session must still perform its normal permission/lifetime checks and durable save.</summary>
    public PresentSharedObjectUpdateResult TryReplaceSharedObject(Guid expectedDocumentId, int expectedDocumentVersion,
        Guid elementId, JsonElement expectedBundle, JsonElement replacementBundle, IPresentSharedObjectBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        var document = Document;
        if (IsLiveTextEditActive)
            return new(false, "TextEditInProgress", "Finish the current text edit before changing a shared drawing.");
        if (document.Id != expectedDocumentId || document.Version != expectedDocumentVersion)
            return new(false, "RevisionConflict", "The Present document changed before the shared edit.");
        var targets = document.Slides.SelectMany(slide => slide.Elements).Where(element => element.Id == elementId).ToArray();
        if (targets.Length != 1 || targets[0].Kind != PresentElementKind.SharedObject ||
            targets[0].SharedProductivityBundle is not { } current)
            return new(false, "SharedObjectUnavailable", "The exact shared object is no longer in this presentation.");
        var target = targets[0];
        if (target.Locked) return new(false, "ObjectLocked", "Unlock the object before editing its drawing.");
        if (!JsonElement.DeepEquals(current, expectedBundle))
            return new(false, "PayloadConflict", "The drawing changed after the edit was prepared.");
        PresentSharedObjectCapture before, after;
        try { before = bridge.Capture(expectedBundle); after = bridge.Capture(replacementBundle); }
        catch (PresentSharedObjectCapabilityException error) { return new(false, error.Code, error.Message); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(false, "InvalidOrUnsupportedObject", error.Message); }
        if (before.ObjectId != elementId || after.ObjectId != before.ObjectId ||
            after.ObjectType != before.ObjectType || after.SchemaVersion != before.SchemaVersion)
            return new(false, "ObjectIdentityMismatch", "A shared edit cannot replace its stable identity or required schema.");
        // Recheck after the owner adapter ran, before Mutate can commit live text or allocate history.
        if (IsLiveTextEditActive || !ReferenceEquals(Document, document) || document.Id != expectedDocumentId || document.Version != expectedDocumentVersion ||
            !document.Slides.SelectMany(slide => slide.Elements).Any(element => ReferenceEquals(element, target)) ||
            target.Locked || target.SharedProductivityBundle is not { } observed || !JsonElement.DeepEquals(observed, expectedBundle))
            return new(false, "PayloadConflict", "The Present owner or drawing changed while preparing the edit.");
        if (JsonElement.DeepEquals(observed, after.Bundle)) return new(false, "NoChanges", "The shared drawing is unchanged.");
        var detached = after.Bundle.Clone();
        Mutate(() => target.SharedProductivityBundle = detached);
        return new(true, "PreparedInDocument", "The canonical drawing changed in the existing undo history; save is still required.", elementId);
    }

    /// <summary>Apply one owner-verified whole-stroke erase across the shared ink objects on one slide.
    /// All changed bundles/removals form one document history entry; persistence remains the owning surface's job.</summary>
    public PresentSharedObjectEraseResult TryApplySharedInkErase(Guid expectedDocumentId, int expectedDocumentVersion,
        Guid selectedSlideId, IReadOnlyList<JsonElement> expectedBundles, IReadOnlyList<JsonElement> replacementBundles,
        IPresentSharedObjectBridge bridge, Func<bool>? finalOwnerValidation = null)
    {
        ArgumentNullException.ThrowIfNull(expectedBundles);
        ArgumentNullException.ThrowIfNull(replacementBundles);
        ArgumentNullException.ThrowIfNull(bridge);
        var document = Document;
        if (IsLiveTextEditActive) return new(false, "TextEditInProgress", "Finish the current text edit before erasing ink.", []);
        if (document.Id != expectedDocumentId || document.Version != expectedDocumentVersion || Selection.SlideId != selectedSlideId)
            return new(false, "RevisionConflict", "The presentation or selected slide changed before erasing ink.", []);
        var slide = document.Slides.SingleOrDefault(value => value.Id == selectedSlideId);
        if (slide is null) return new(false, "SlideNotFound", "The original slide no longer exists.", []);

        var original = new Dictionary<Guid, (PresentElement Element, JsonElement Payload)>();
        var replacements = new Dictionary<Guid, JsonElement>();
        try
        {
            foreach (var bundle in expectedBundles)
            {
                var capture = bridge.Capture(bundle);
                if (capture.ObjectId == Guid.Empty || capture.ObjectType != "drawing.ink" || capture.SchemaVersion != 1)
                    return new(false, "ObjectIdentityMismatch", "The original drawing has invalid ink identity or schema.", []);
                var element = slide.Elements.SingleOrDefault(value => value.Id == capture.ObjectId &&
                    value.Kind == PresentElementKind.SharedObject && value.SharedProductivityBundle is not null);
                if (element is null)
                    return new(false, "SharedObjectUnavailable", "An original ink object no longer belongs to the selected slide.", []);
                if (!JsonElement.DeepEquals(element.SharedProductivityBundle!.Value, bundle))
                    return new(false, "PayloadConflict", "An original ink object changed before erasure.", []);
                if (!original.TryAdd(capture.ObjectId, (element, bundle.Clone())))
                    return new(false, "ObjectIdentityMismatch", "The original drawing repeats an ink identity.", []);
            }
            foreach (var bundle in replacementBundles)
            {
                var capture = bridge.Capture(bundle);
                if (capture.ObjectId == Guid.Empty || capture.ObjectType != "drawing.ink" || capture.SchemaVersion != 1 ||
                    !original.ContainsKey(capture.ObjectId) || !replacements.TryAdd(capture.ObjectId, capture.Bundle.Clone()))
                    return new(false, "ObjectIdentityMismatch", "The erase result changed or duplicated a drawing identity.", []);
            }
        }
        catch (PresentSharedObjectCapabilityException error) { return new(false, error.Code, error.Message, []); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(false, "InvalidOrUnsupportedObject", error.Message, []); }

        var changed = original.Where(pair => !replacements.TryGetValue(pair.Key, out var next) ||
            !JsonElement.DeepEquals(pair.Value.Payload, next)).ToArray();
        if (changed.Any(pair => pair.Value.Element.Locked))
            return new(false, "ObjectLocked", "Unlock every crossed drawing before erasing.", []);
        if (changed.Length == 0) return new(false, "NoChanges", "No ink strokes intersected the eraser gesture.", []);

        if (IsLiveTextEditActive || !ReferenceEquals(Document, document) || document.Id != expectedDocumentId ||
            document.Version != expectedDocumentVersion || Selection.SlideId != selectedSlideId ||
            !document.Slides.Any(currentSlide => ReferenceEquals(currentSlide, slide)) ||
            original.Any(pair => !slide.Elements.Any(value => ReferenceEquals(value, pair.Value.Element) &&
                value.SharedProductivityBundle is { } payload && JsonElement.DeepEquals(payload, pair.Value.Payload))) ||
            changed.Any(pair => pair.Value.Element.Locked))
            return new(false, "RevisionConflict", "The original drawing changed while the erase was prepared.", []);

        var changedIds = changed.Select(pair => pair.Key).ToArray();
        // The owning surface may need to revalidate host placement or other state that is
        // deliberately outside this application model. Invoke it after all bridge calls,
        // immediately before this single history mutation.
        if (finalOwnerValidation is not null)
        {
            bool isCurrent;
            try { isCurrent = finalOwnerValidation(); }
            catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or
                NotSupportedException or InvalidOperationException or OverflowException)
            { return new(false, "RevisionConflict", "The original owner could not revalidate the erase before commit: " + error.Message, []); }
            if (!isCurrent || changed.Any(pair => pair.Value.Element.Locked))
                return new(false, "RevisionConflict", "The original owner or ink placement changed before the erase commit.", []);
        }
        // A validator may synchronously publish or reenter application state. Repeat
        // the editor-side CAS/payload checks after it returns; no callback sits between
        // this last check and the single Mutate below.
        if (IsLiveTextEditActive || !ReferenceEquals(Document, document) || document.Id != expectedDocumentId ||
            document.Version != expectedDocumentVersion || Selection.SlideId != selectedSlideId ||
            !document.Slides.Any(currentSlide => ReferenceEquals(currentSlide, slide)) ||
            original.Any(pair => !slide.Elements.Any(value => ReferenceEquals(value, pair.Value.Element) &&
                value.SharedProductivityBundle is { } payload && JsonElement.DeepEquals(payload, pair.Value.Payload))) ||
            changed.Any(pair => pair.Value.Element.Locked))
            return new(false, "RevisionConflict", "The presentation changed during final erase validation.", []);
        Mutate(() =>
        {
            foreach (var pair in changed)
            {
                if (replacements.TryGetValue(pair.Key, out var replacement)) pair.Value.Element.SharedProductivityBundle = replacement.Clone();
                else slide.Elements.Remove(pair.Value.Element);
            }
        });
        return new(true, "PreparedInDocument", "The canonical ink erase is in one undo transaction; save is still required.", changedIds);
    }

    private static void DemandLegacySharedObjectCloneSupported(IEnumerable<PresentElement> elements)
    {
        if (elements.Any(element => element.Kind == PresentElementKind.SharedObject || element.SharedProductivityBundle is not null))
            throw new PresentSharedObjectCapabilityException("SharedObjectCloneProviderRequired",
                "Use the Home shared-object paste operation so embedded identities are cloned by their canonical owner.");
    }
}

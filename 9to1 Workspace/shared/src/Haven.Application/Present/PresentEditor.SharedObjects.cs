using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>App-neutral transport identity, validated by the actual Home object owner.</summary>
public sealed record PresentSharedObjectCapture(Guid ObjectId, string ObjectType, int SchemaVersion, JsonElement Bundle);
public sealed record PresentSharedObjectSupport(bool Compatible, bool CanRender, string Code, string Message);
public sealed record PresentSharedObjectInsertionResult(bool Inserted, string Code, string Message, Guid? ElementId = null);

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

    private static void DemandLegacySharedObjectCloneSupported(IEnumerable<PresentElement> elements)
    {
        if (elements.Any(element => element.Kind == PresentElementKind.SharedObject || element.SharedProductivityBundle is not null))
            throw new PresentSharedObjectCapabilityException("SharedObjectCloneProviderRequired",
                "Use the Home shared-object paste operation so embedded identities are cloned by their canonical owner.");
    }
}

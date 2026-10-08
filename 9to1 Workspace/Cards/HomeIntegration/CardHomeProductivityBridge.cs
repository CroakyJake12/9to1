using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Cards.HomeIntegration;

/// <summary>
/// A Cards-specific view over the existing Home-owned Shared Productivity Engine.
/// It does not reimplement object schemas, renderers, mutations or persistence.
/// </summary>
public static class CardHomeProductivityBridge
{
    public const string BundleFormat = "9to1.home-productivity-bundle/1";

    public static CardSide CaptureBundle(HomeProductivityObjectBundle bundle,
        string searchText = "", string? fontFamily = null, string? backgroundColor = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.FormatVersion != 1 || bundle.Objects is null || bundle.Styles is null)
            throw new CardOperationException(CardFailureCode.InvalidContent,
                "The shared Home productivity bundle is incomplete or unsupported.");

        return new CardSide
        {
            DocumentFormat = BundleFormat,
            Document = JsonSerializer.SerializeToElement(bundle),
            SearchText = searchText,
            FontFamily = fontFamily,
            BackgroundColor = backgroundColor,
        };
    }

    public static CardProductivityInspection Inspect(CardSide side,
        IHomeProductivityEngine engine, Guid setId, long setRevision,
        string appVersion = "development")
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(engine);
        side.Validate();
        if (setId == Guid.Empty || setRevision < 1)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "A valid canonical set and revision are required for shared-engine inspection.");

        // Pre-existing card payloads in a different format remain intact.
        // Explicitly report unsupported content instead of silently flattening it.
        if (!string.Equals(side.DocumentFormat, BundleFormat, StringComparison.Ordinal))
            return new(false, "DocumentAdapterRequired", engine.EngineVersion, []);

        HomeProductivityObjectBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<HomeProductivityObjectBundle>(side.Document.GetRawText());
        }
        catch (JsonException)
        {
            return new(false, "InvalidSharedBundle", engine.EngineVersion, []);
        }

        if (bundle is null || bundle.FormatVersion != 1 || bundle.Objects is null
            || bundle.Styles is null)
            return new(false, "InvalidSharedBundle", engine.EngineVersion, []);

        string[] required = bundle.Objects.Select(item => item.ObjectType)
            .Distinct(StringComparer.Ordinal).ToArray();
        HomeProductivityCompatibility supported =
            engine.GetCompatibility("cards", appVersion, required);
        if (!supported.Compatible)
            return new(false, "RequiredSchemaUnsupported", supported.EngineVersion,
                supported.UnsupportedRequiredTypes);

        var target = new HomeProductivityContext("cards", setId.ToString("D"), setRevision,
            [], engine.ListObjectTypes().Select(schema => schema.ObjectType)
                .ToHashSet(StringComparer.Ordinal));
        bool compatible = engine.CanPaste(bundle, target, out string reason);
        return new(compatible, compatible ? "Compatible" : reason,
            supported.EngineVersion, supported.UnsupportedRequiredTypes);
    }
}

public sealed record CardProductivityInspection(bool Compatible,
    string Code, int EngineVersion, IReadOnlyList<string> UnsupportedObjectTypes);

using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Uses the currently selected Home model to propose bounded, filled vector geometry
/// from a section name. It sends no Board body, page content, or other context.</summary>
public sealed class BoardsSectionIconSuggestionService : IBoardsSectionIconGenerationHost
{
    private const int MaximumResponseBytes = 8 * 1024;
    private const string SystemPrompt = """
        Create one small, filled, rounded vector icon for a 9to1 Boards section.
        Return exactly one JSON object and no markdown or explanation, with this schema:
        {"icons":[{"shapes":[{"kind":"roundRect","x":3,"y":3,"width":18,"height":18,"radius":3}]}]}
        A shape may be one of:
        - roundRect: x, y, width, height, radius
        - circle: cx, cy, radius
        - polygon: points:[[x,y],...], optional cutout:true
        The canvas is 24 by 24. Coordinates and dimensions must be finite and stay inside it.
        Use 1 to 12 shapes total, and at most 64 polygon vertices total; each polygon has 3 to 12
        vertices. Use at least one visible (not cutout) shape. Shapes inherit one fill color from the
        user, so use overlap and cutout polygons for clear negative space. Prefer a recognizable
        centered silhouette with moderate corner rounding. Do not return SVG, paths, colors, strokes,
        text, URLs, scripts, external assets, comments, or properties outside the schema.
        Return three visually distinct alternatives when possible, and never return more than three.
        Each icon has its own 12-shape and 64-polygon-vertex limits. The complete response must be
        small enough to fit an 8 KiB JSON envelope.
        """;

    private readonly IProviderModelClient _models;
    private readonly HomeModelSelectionSource _selection;
    private readonly string _profileId;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationToken _tabLifetime;
    private readonly Func<bool> _tabIsCurrent;
    private long _generation;

    public BoardsSectionIconSuggestionService(IProviderModelClient models,
        HomeModelSelectionSource selection, string profileId, CancellationToken appLifetime,
        CancellationToken tabLifetime, Func<bool> tabIsCurrent)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _profileId = profileId;
        if (!appLifetime.CanBeCanceled || !tabLifetime.CanBeCanceled)
            throw new ArgumentException("The actual application and Boards tab lifetimes are required.");
        _appLifetime = appLifetime;
        _tabLifetime = tabLifetime;
        _tabIsCurrent = tabIsCurrent ?? throw new ArgumentNullException(nameof(tabIsCurrent));
    }

    public bool CanGenerate => !_appLifetime.IsCancellationRequested &&
        !_tabLifetime.IsCancellationRequested && _tabIsCurrent();

    public async Task<BoardsGeneratedSectionIconResult> GenerateAsync(string sectionTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionTitle);
        var title = sectionTitle.Trim();
        if (title.Length > 120) title = title[..120];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _appLifetime, _tabLifetime, cancellationToken);
        var token = linked.Token;
        DemandCurrent(token);

        var before = await _selection.ResolveAsync(Interlocked.Increment(ref _generation), token)
            .ConfigureAwait(false);
        if (before?.Actor is null || !StringComparer.Ordinal.Equals(before.Actor.ProfileId, _profileId) ||
            string.IsNullOrWhiteSpace(before.ProviderId) || string.IsNullOrWhiteSpace(before.ModelId))
            throw new InvalidOperationException("Select a current Home model before generating a section icon.");

        var modelKey = before.ProviderId + ":" + before.ModelId;
        var prompt = $"Create a filled section icon representing this section name: {JsonSerializer.Serialize(title)}";
        var request = new OllamaChatRequest(
            modelKey,
            [new OllamaMessage("user", prompt)],
            EffortLevel.Medium,
            SystemPrompt,
            Options: new GenerationOptions(0.2, 4096, 0)
            {
                RequestedContextConstraints = new ModelRequestContextConstraints(AllowPersistentMemoryRead: false),
                RequestedRoutingConstraints = new ModelRequestRoutingConstraints(AllowFallback: false)
            })
        {
            ReasoningId = before.SelectedReasoningId,
            ReasoningTargetProviderId = before.ProviderId,
            ReasoningTargetModelName = before.ModelId,
        };
        var response = await _models.CompleteAsync(request, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        DemandCurrent(token);

        var after = await _selection.ResolveAsync(Interlocked.Increment(ref _generation), token)
            .ConfigureAwait(false);
        if (!SameSelection(before, after, _profileId))
            throw new InvalidOperationException("The selected Home model changed while the icon was being created. Try again.");

        return new BoardsGeneratedSectionIconResult(ParseResponse(response), before.ProviderId, before.ModelId);
    }

    internal static IReadOnlyList<HavenRichSectionGeneratedIcon> ParseResponse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Encoding.UTF8.GetByteCount(response) is 0 or > MaximumResponseBytes)
            throw new InvalidDataException("The model response exceeded the generated icon size limit.");

        try
        {
            using var document = JsonDocument.Parse(response, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            RequireObjectProperties(root, "icons");
            if (!root.TryGetProperty("icons", out var rawIcons) || rawIcons.ValueKind != JsonValueKind.Array ||
                rawIcons.GetArrayLength() is < 1 or > 3)
                throw new InvalidDataException("The model response must contain one to three icon alternatives.");
            var candidates = new List<HavenRichSectionGeneratedIcon>(rawIcons.GetArrayLength());
            foreach (var rawIcon in rawIcons.EnumerateArray())
            {
                RequireObjectProperties(rawIcon, "shapes");
                if (!rawIcon.TryGetProperty("shapes", out var rawShapes) || rawShapes.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Each icon alternative must contain a shapes array.");
                var shapes = new List<HavenRichSectionIconShape>();
                foreach (var rawShape in rawShapes.EnumerateArray())
                {
                    if (shapes.Count == HavenRichSectionIconGeometry.MaximumShapes)
                        throw new InvalidDataException("The model returned too many icon shapes.");
                    if (rawShape.ValueKind != JsonValueKind.Object || !rawShape.TryGetProperty("kind", out var kindValue) ||
                        kindValue.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Each generated icon shape must have a known kind.");
                    shapes.Add(kindValue.GetString() switch
                    {
                        "roundRect" => ParseRoundRect(rawShape),
                        "circle" => ParseCircle(rawShape),
                        "polygon" => ParsePolygon(rawShape),
                        _ => throw new InvalidDataException("The model returned an unsupported icon primitive."),
                    });
                }
                var icon = new HavenRichSectionGeneratedIcon { Shapes = shapes };
                HavenRichSectionIconGeometry.Validate(icon);
                candidates.Add(HavenRichSectionIconGeometry.Copy(icon));
            }
            return Array.AsReadOnly(candidates.ToArray());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The model did not return valid bounded icon data.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The model returned invalid vector geometry.", exception);
        }
    }

    private static HavenRichSectionIconRoundRect ParseRoundRect(JsonElement shape)
    {
        RequireObjectProperties(shape, "kind", "x", "y", "width", "height", "radius");
        return new(ReadNumber(shape, "x"), ReadNumber(shape, "y"), ReadNumber(shape, "width"),
            ReadNumber(shape, "height"), ReadNumber(shape, "radius"));
    }

    private static HavenRichSectionIconCircle ParseCircle(JsonElement shape)
    {
        RequireObjectProperties(shape, "kind", "cx", "cy", "radius");
        return new(ReadNumber(shape, "cx"), ReadNumber(shape, "cy"), ReadNumber(shape, "radius"));
    }

    private static HavenRichSectionIconPolygon ParsePolygon(JsonElement shape)
    {
        if (shape.TryGetProperty("cutout", out _))
            RequireObjectProperties(shape, "kind", "points", "cutout");
        else
            RequireObjectProperties(shape, "kind", "points");
        if (!shape.TryGetProperty("points", out var rawPoints) || rawPoints.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Generated polygons need a point array.");
        var points = new List<HavenRichSectionIconPoint>();
        foreach (var rawPoint in rawPoints.EnumerateArray())
        {
            if (rawPoint.ValueKind != JsonValueKind.Array || rawPoint.GetArrayLength() != 2)
                throw new InvalidDataException("Each generated polygon point needs exactly two coordinates.");
            var pair = rawPoint.EnumerateArray();
            pair.MoveNext();
            var x = ReadNumber(pair.Current);
            pair.MoveNext();
            points.Add(new(x, ReadNumber(pair.Current)));
        }
        var isCutout = shape.TryGetProperty("cutout", out var rawCutout) &&
            rawCutout.ValueKind == JsonValueKind.True;
        if (shape.TryGetProperty("cutout", out rawCutout) && rawCutout.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("The polygon cutout flag must be a boolean.");
        return new(points, isCutout);
    }

    private static double ReadNumber(JsonElement owner, string property) =>
        owner.TryGetProperty(property, out var value) ? ReadNumber(value) :
            throw new InvalidDataException($"Generated icon field '{property}' is missing.");

    private static double ReadNumber(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new InvalidDataException("Generated icon coordinates must be finite numbers.");
        return result;
    }

    private static void RequireObjectProperties(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Generated icon data must be a JSON object.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length || !actual.Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The model response contained unknown or missing icon fields.");
    }

    private static bool SameSelection(HavenModelProjectionSnapshot before, HavenModelProjectionSnapshot? after,
        string profileId) => after?.Actor is { } actor &&
        StringComparer.Ordinal.Equals(actor.ProfileId, profileId) && Equals(before.Actor, after.Actor) &&
        StringComparer.Ordinal.Equals(before.AuthenticationRevision, after.AuthenticationRevision) &&
        StringComparer.Ordinal.Equals(before.ScopeName, after.ScopeName) &&
        StringComparer.Ordinal.Equals(before.ScopeId, after.ScopeId) &&
        StringComparer.Ordinal.Equals(before.RouteId, after.RouteId) && before.RouteRevision == after.RouteRevision &&
        StringComparer.Ordinal.Equals(before.ProviderId, after.ProviderId) &&
        StringComparer.Ordinal.Equals(before.ModelId, after.ModelId) &&
        before.IsLocal == after.IsLocal && before.LocalityKnown == after.LocalityKnown &&
        StringComparer.Ordinal.Equals(before.SelectedReasoningId, after.SelectedReasoningId);

    private void DemandCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!CanGenerate)
            throw new OperationCanceledException("The Boards tab is no longer current.", token);
    }
}

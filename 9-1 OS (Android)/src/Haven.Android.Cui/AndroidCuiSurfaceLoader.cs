using System.Globalization;
using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace Haven.Android.Cui;

public enum AndroidCuiStackDirection
{
    Vertical,
    Horizontal
}

public enum AndroidCuiTextRole
{
    Body,
    Title,
    Caption
}

public abstract record AndroidCuiNode(string? Id);

public sealed record AndroidCuiStackNode(
    string? Id,
    AndroidCuiStackDirection Direction,
    double Gap,
    IReadOnlyList<AndroidCuiNode> Children) : AndroidCuiNode(Id);

public sealed record AndroidCuiTextNode(
    string? Id,
    string Text,
    AndroidCuiTextRole Role) : AndroidCuiNode(Id);

public sealed record AndroidCuiButtonNode(
    string? Id,
    string Text,
    string Action) : AndroidCuiNode(Id);

/// <summary>A validated, bounded CUI surface that can be materialized by the Android host.</summary>
public sealed record AndroidCuiSurface(
    string SourceName,
    string? Id,
    IReadOnlyList<AndroidCuiNode> Children);

/// <summary>
/// Asynchronous source seam used by Android assets, tests, and future content providers.
/// Implementations only provide text; this loader owns format enforcement and validation.
/// </summary>
public interface IAndroidCuiSource
{
    ValueTask<string> ReadAsync(string sourceName, CancellationToken cancellationToken);
}

public sealed class AndroidCuiSurfaceException(string sourceName, string message)
    : FormatException($"{sourceName}: {message}")
{
    public string SourceName { get; } = sourceName;
}

/// <summary>
/// Loads native .cui markup and projects its small Android-supported vocabulary into a
/// platform-neutral surface plan. This boundary never accepts AXAML or legacy HUI input.
/// </summary>
public sealed class AndroidCuiSurfaceLoader
{
    private static readonly IReadOnlySet<string> RootAttributes = AttributeSet("id");
    private static readonly IReadOnlySet<string> StackAttributes = AttributeSet("id", "orientation", "gap");
    private static readonly IReadOnlySet<string> TextAttributes = AttributeSet("id", "role");
    private static readonly IReadOnlySet<string> ButtonAttributes = AttributeSet("id", "action");

    private readonly CuiRichParser _parser;

    public AndroidCuiSurfaceLoader(CuiRichParser? parser = null)
    {
        _parser = parser ?? new CuiRichParser();
    }

    public AndroidCuiSurface Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        CuiProjectItems.RequireCui(fullPath);
        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    public async ValueTask<AndroidCuiSurface> LoadAsync(
        string sourceName,
        IAndroidCuiSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(source);

        // Reject the identity before asking a platform source to open anything.
        CuiProjectItems.RequireCui(sourceName);
        var markup = await source.ReadAsync(sourceName, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(markup, sourceName);
    }

    public AndroidCuiSurface Parse(string markup, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        // CuiRichParser repeats this check by design. Keeping it at the Android edge
        // ensures legacy files cannot be interpreted even if parser behavior changes.
        CuiProjectItems.RequireCui(sourceName);
        var document = _parser.Parse(markup, sourceName);
        ValidateRootAttributes(document, RootAttributes, sourceName);

        return new AndroidCuiSurface(
            document.SourceName,
            OptionalRootAttribute(document, "id"),
            document.Components.Select(child => Project(child, sourceName)).ToArray());
    }

    private static AndroidCuiNode Project(CuiComponent element, string sourceName) => element.Type switch
    {
        "Stack" => ProjectStack(element, sourceName),
        "Text" => ProjectText(element, sourceName),
        "Button" => ProjectButton(element, sourceName),
        _ => throw new AndroidCuiSurfaceException(
            sourceName,
            $"Element <{element.Type}> is not supported by the bounded Android CUI surface.")
    };

    private static AndroidCuiStackNode ProjectStack(CuiComponent element, string sourceName)
    {
        ValidateAttributes(element, StackAttributes, sourceName);
        ValidateNoText(element, sourceName);

        var direction = OptionalAttribute(element, "orientation")?.ToLowerInvariant() switch
        {
            null or "vertical" => AndroidCuiStackDirection.Vertical,
            "horizontal" => AndroidCuiStackDirection.Horizontal,
            var value => throw new AndroidCuiSurfaceException(
                sourceName,
                $"Stack orientation '{value}' must be 'vertical' or 'horizontal'.")
        };

        var gap = ParseGap(OptionalAttribute(element, "gap"), sourceName);
        return new AndroidCuiStackNode(
            OptionalAttribute(element, "id"),
            direction,
            gap,
            element.Children.Select(child => Project(child, sourceName)).ToArray());
    }

    private static AndroidCuiTextNode ProjectText(CuiComponent element, string sourceName)
    {
        ValidateAttributes(element, TextAttributes, sourceName);
        ValidateLeaf(element, sourceName);

        var role = OptionalAttribute(element, "role")?.ToLowerInvariant() switch
        {
            null or "body" => AndroidCuiTextRole.Body,
            "title" => AndroidCuiTextRole.Title,
            "caption" => AndroidCuiTextRole.Caption,
            var value => throw new AndroidCuiSurfaceException(
                sourceName,
                $"Text role '{value}' must be 'body', 'title', or 'caption'.")
        };

        return new AndroidCuiTextNode(OptionalAttribute(element, "id"), element.Text, role);
    }

    private static AndroidCuiButtonNode ProjectButton(CuiComponent element, string sourceName)
    {
        ValidateAttributes(element, ButtonAttributes, sourceName);
        ValidateLeaf(element, sourceName);
        var action = RequiredAttribute(element, "action", sourceName);
        if (string.IsNullOrWhiteSpace(element.Text))
            throw new AndroidCuiSurfaceException(sourceName, "Button text cannot be empty.");

        return new AndroidCuiButtonNode(OptionalAttribute(element, "id"), element.Text, action);
    }

    private static double ParseGap(string? value, string sourceName)
    {
        if (value is null)
            return 0;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gap)
            || !double.IsFinite(gap)
            || gap < 0)
            throw new AndroidCuiSurfaceException(sourceName, $"Stack gap '{value}' must be a finite non-negative number.");
        return gap;
    }

    private static void ValidateAttributes(
        CuiComponent element,
        IReadOnlySet<string> supported,
        string sourceName)
    {
        var unsupported = element.AuthoredAttributeNames().FirstOrDefault(attribute => !supported.Contains(attribute));
        if (unsupported is not null)
            throw new AndroidCuiSurfaceException(
                sourceName,
                $"Attribute '{unsupported}' is not supported on <{element.Type}> by the Android CUI surface.");
    }

    private static void ValidateRootAttributes(
        CuiDocument document,
        IReadOnlySet<string> supported,
        string sourceName)
    {
        var unsupported = document.RootProperties.Keys.FirstOrDefault(attribute => !supported.Contains(attribute));
        if (unsupported is not null)
            throw new AndroidCuiSurfaceException(
                sourceName,
                $"Attribute '{unsupported}' is not supported on <Cui> by the Android CUI surface.");
    }

    private static void ValidateNoText(CuiComponent element, string sourceName)
    {
        if (!string.IsNullOrWhiteSpace(element.Text))
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Type}> cannot contain direct text.");
    }

    private static void ValidateLeaf(CuiComponent element, string sourceName)
    {
        if (element.Children.Count != 0)
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Type}> cannot contain child elements.");
    }

    private static string RequiredAttribute(CuiComponent element, string name, string sourceName)
    {
        var value = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Name}> requires a non-empty '{name}' attribute.");
        return value;
    }

    private static string? OptionalAttribute(CuiComponent element, string name) =>
        element.TryGetLiteralAttribute(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? OptionalRootAttribute(CuiDocument document, string name) =>
        document.RootProperties.TryGetValue(name, out var value) && value is CuiLiteralValue literal
            && !string.IsNullOrWhiteSpace(literal.Value)
                ? literal.Value.Trim()
                : null;

    private static IReadOnlySet<string> AttributeSet(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}

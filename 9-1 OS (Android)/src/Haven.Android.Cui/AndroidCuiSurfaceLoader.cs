using System.Globalization;
using CakeOS.Cui.Markup;

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

    private readonly CuiMarkupParser _parser;

    public AndroidCuiSurfaceLoader(CuiMarkupParser? parser = null)
    {
        _parser = parser ?? new CuiMarkupParser();
    }

    public AndroidCuiSurface Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        CuiMarkupFormat.RequireCuiInput(fullPath);
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
        CuiMarkupFormat.RequireCuiInput(sourceName);
        var markup = await source.ReadAsync(sourceName, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(markup, sourceName);
    }

    public AndroidCuiSurface Parse(string markup, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        // CuiMarkupParser repeats this check by design. Keeping it at the Android edge
        // ensures legacy files cannot be interpreted even if parser behavior changes.
        CuiMarkupFormat.RequireCuiInput(sourceName);
        var document = _parser.Parse(markup, sourceName);
        ValidateAttributes(document.Root, RootAttributes, sourceName);
        ValidateNoText(document.Root, sourceName);

        return new AndroidCuiSurface(
            document.SourceName,
            OptionalAttribute(document.Root, "id"),
            document.Root.Children.Select(child => Project(child, sourceName)).ToArray());
    }

    private static AndroidCuiNode Project(CuiElement element, string sourceName) => element.Name switch
    {
        "Stack" => ProjectStack(element, sourceName),
        "Text" => ProjectText(element, sourceName),
        "Button" => ProjectButton(element, sourceName),
        _ => throw new AndroidCuiSurfaceException(
            sourceName,
            $"Element <{element.Name}> is not supported by the bounded Android CUI surface.")
    };

    private static AndroidCuiStackNode ProjectStack(CuiElement element, string sourceName)
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

    private static AndroidCuiTextNode ProjectText(CuiElement element, string sourceName)
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

    private static AndroidCuiButtonNode ProjectButton(CuiElement element, string sourceName)
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
        CuiElement element,
        IReadOnlySet<string> supported,
        string sourceName)
    {
        var unsupported = element.Attributes.Keys.FirstOrDefault(attribute => !supported.Contains(attribute));
        if (unsupported is not null)
            throw new AndroidCuiSurfaceException(
                sourceName,
                $"Attribute '{unsupported}' is not supported on <{element.Name}> by the Android CUI surface.");
    }

    private static void ValidateNoText(CuiElement element, string sourceName)
    {
        if (!string.IsNullOrWhiteSpace(element.Text))
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Name}> cannot contain direct text.");
    }

    private static void ValidateLeaf(CuiElement element, string sourceName)
    {
        if (element.Children.Count != 0)
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Name}> cannot contain child elements.");
    }

    private static string RequiredAttribute(CuiElement element, string name, string sourceName)
    {
        var value = OptionalAttribute(element, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new AndroidCuiSurfaceException(sourceName, $"Element <{element.Name}> requires a non-empty '{name}' attribute.");
        return value;
    }

    private static string? OptionalAttribute(CuiElement element, string name) =>
        element.Attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static IReadOnlySet<string> AttributeSet(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}

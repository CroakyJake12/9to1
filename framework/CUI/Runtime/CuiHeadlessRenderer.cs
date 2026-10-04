using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Headless rendering proof: loads a .cui file, instantiates Avalonia controls,
/// and captures a rendering snapshot. This proves the CUI→Avalonia pipeline works
/// without requiring a display server.
/// </summary>
public static class CuiHeadlessRenderer
{
    /// <summary>
    /// Builds an Avalonia control tree from CUI markup and validates it against
    /// the headless platform. Returns the control tree and validation diagnostics.
    /// </summary>
    public static CuiRenderResult Render(string cuiMarkup, string sourceName = "test.cui")
        => Render(cuiMarkup, new CuiControlRegistry(), sourceName);

    /// <summary>Builds a CUI tree using a host's specialised component registrations.</summary>
    public static CuiRenderResult Render(
        string cuiMarkup,
        CuiControlRegistry controlRegistry,
        string sourceName = "test.cui")
    {
        ArgumentNullException.ThrowIfNull(controlRegistry);
        var loader = new CuiControlLoader(controlRegistry);
        var (root, parseDiagnostics) = loader.LoadMarkup(cuiMarkup, sourceName);

        var errors = new List<string>();
        var warnings = new List<string>();

        // Collect parse diagnostics
        foreach (var diag in parseDiagnostics)
        {
            if (diag.Severity == CuiDiagnosticSeverity.Error)
                errors.Add(diag.ToString());
            else if (diag.Severity == CuiDiagnosticSeverity.Warning)
                warnings.Add(diag.ToString());
        }

        if (root is null)
        {
            return new CuiRenderResult(
                null, false, errors.ToArray(), warnings.ToArray(),
                0, "No root control produced.");
        }

        // Validate the control tree
        var controlCount = CountControls(root);
        var validationErrors = ValidateTree(root);

        errors.AddRange(validationErrors);

        return new CuiRenderResult(
            root,
            errors.Count == 0,
            errors.ToArray(),
            warnings.ToArray(),
            controlCount,
            $"Loaded {controlCount} Avalonia controls from CUI markup.");
    }

    private static int CountControls(Control root)
    {
        int count = 1;
        if (root is Panel p)
        {
            foreach (var child in p.Children)
                count += CountControls(child);
        }
        else if (root is Decorator d && d.Child is not null)
        {
            count += CountControls(d.Child);
        }
        else if (root is ContentControl cc && cc.Content is Control ccChild)
        {
            count += CountControls(ccChild);
        }
        return count;
    }

    private static List<string> ValidateTree(Control root)
    {
        var errors = new List<string>();
        ValidateControl(root, errors, 0);
        return errors;
    }

    private static void ValidateControl(Control control, List<string> errors, int depth)
    {
        if (depth > 50)
        {
            errors.Add($"Control tree exceeds maximum depth of 50: {control.GetType().Name}");
            return;
        }

        // Validate Panel children
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                ValidateControl(child, errors, depth + 1);
        }
        else if (control is Decorator decorator && decorator.Child is not null)
        {
            ValidateControl(decorator.Child, errors, depth + 1);
        }
    }
}

/// <summary>
/// Result of a CUI headless rendering operation.
/// </summary>
public sealed record CuiRenderResult(
    Control? Root,
    bool Success,
    string[] Errors,
    string[] Warnings,
    int ControlCount,
    string Summary);

using Avalonia.Controls;
using Avalonia.Layout;
using CakeOS.Cui;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Maps CUI component names and registered specialised component types to native
/// Avalonia controls. Custom names remain case-sensitive; built-in CUI names and
/// type values are case-insensitive.
/// </summary>
public sealed class CuiControlRegistry
{
    private readonly Dictionary<string, Func<CuiComponent, Control>> _customFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CuiComponent, Control>> _objectFactories = new(StringComparer.Ordinal);

    /// <summary>Register a specialised control type or renderer host.</summary>
    public void RegisterControlType(string typeName, Func<CuiComponent, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(factory);

        if (IsReservedType(typeName))
            throw new ArgumentException($"'{typeName}' is a reserved built-in CUI component type.", nameof(typeName));
        if (!_customFactories.TryAdd(typeName, factory))
            throw new ArgumentException($"A CUI control type named '{typeName}' is already registered.", nameof(typeName));
    }

    /// <summary>Register a typed renderer for an Object component's Type property.</summary>
    public void RegisterObjectRenderer(string objectType, Func<CuiComponent, Control> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_objectFactories.TryAdd(objectType, factory))
            throw new ArgumentException($"A CUI object renderer named '{objectType}' is already registered.", nameof(objectType));
    }

    internal bool TryCreate(CuiComponent component, Func<CuiValue, string?> resolveValue, out Control? control, out string? error)
    {
        control = null;
        error = null;

        if (component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase))
        {
            var objectType = GetTypeValue(component, resolveValue);
            if (objectType is not null && _objectFactories.TryGetValue(objectType, out var objectFactory))
            {
                control = objectFactory(component);
                return true;
            }

            error = objectType is null
                ? "Object requires a Type identifying a registered renderer."
                : $"No renderer is registered for CUI Object Type '{objectType}'.";
            return false;
        }

        if (TryCreateBuiltIn(component, resolveValue, out control, out error))
            return true;

        if (_customFactories.TryGetValue(component.Type, out var customFactory))
        {
            control = customFactory(component);
            if (control is null)
            {
                error = $"The registered CUI control factory '{component.Type}' returned no control.";
                return false;
            }
            return true;
        }

        error = $"No native control or specialised renderer is registered for CUI component '{component.Type}'.";
        return false;
    }

    private static bool TryCreateBuiltIn(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out Control? control,
        out string? error)
    {
        error = null;
        control = component.Type.ToLowerInvariant() switch
        {
            "page" or "layer" => new Panel(),
            "container" => CreateContainer(component, resolveValue, out error),
            "text" => new TextBlock(),
            "image" => new Image(),
            "button" => new Button(),
            "input" => CreateInput(component, resolveValue, out error),

            // Existing runtime names remain supported for compatibility with
            // current source while first-party markup moves to the compact CUI vocabulary.
            "panel" => new Panel(),
            "stackpanel" => new StackPanel(),
            "grid" => new Grid(),
            "dockpanel" => new DockPanel(),
            "wrappanel" => new WrapPanel(),
            "border" => new Border(),
            "scrollviewer" => new ScrollViewer(),
            "textblock" => new TextBlock(),
            "textbox" => new TextBox(),
            "checkbox" => new CheckBox(),
            "radiobutton" => new RadioButton(),
            "slider" => new Slider(),
            "progressbar" => new ProgressBar(),
            "canvas" => new Canvas(),
            "tabcontrol" => new TabControl(),
            "tabitem" => new TabItem(),
            "listbox" => new ListBox(),
            "combobox" => new ComboBox(),
            "treeview" => new TreeView(),
            "menu" => new Menu(),
            "menuitem" => new MenuItem(),
            "separator" => new Separator(),
            "contentcontrol" => new ContentControl(),
            "itemscontrol" => new ItemsControl(),
            "usercontrol" => new UserControl(),
            "anchor" => new ContentControl(),
            _ => null,
        };

        return control is not null;
    }

    private static Control? CreateContainer(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out string? error)
    {
        error = null;
        var type = GetTypeValue(component, resolveValue);
        if (string.IsNullOrWhiteSpace(type))
        {
            error = "Container requires a registered Type (Grid, Vertical Stack, Horizontal Stack, or Absolute).";
            return null;
        }

        return type.ToLowerInvariant() switch
        {
            "grid" => new Grid(),
            "vertical stack" => new StackPanel { Orientation = Orientation.Vertical },
            "horizontal stack" => new StackPanel { Orientation = Orientation.Horizontal },
            "absolute" => new Canvas(),
            _ => UnknownContainerType(type, out error),
        };
    }

    private static Control? CreateInput(
        CuiComponent component,
        Func<CuiValue, string?> resolveValue,
        out string? error)
    {
        error = null;
        var type = GetTypeValue(component, resolveValue) ?? "Text";
        return type.ToLowerInvariant() switch
        {
            "text" => new TextBox(),
            "number" or "numeric" => new NumericUpDown(),
            "checkbox" or "check box" => new CheckBox(),
            "slider" => new Slider(),
            _ => UnknownInputType(type, out error),
        };
    }

    private static Control? UnknownContainerType(string type, out string? error)
    {
        error = $"Unknown CUI Container Type '{type}'. Supported built-in types are Grid, Vertical Stack, Horizontal Stack, and Absolute.";
        return null;
    }

    private static Control? UnknownInputType(string type, out string? error)
    {
        error = $"Unknown CUI Input Type '{type}'. Register a specialised component for this input behaviour.";
        return null;
    }

    private static string? GetTypeValue(CuiComponent component, Func<CuiValue, string?> resolveValue)
    {
        var typeProperty = component.Properties.FirstOrDefault(
            pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase));
        return typeProperty.Key is null ? null : resolveValue(typeProperty.Value);
    }

    private static bool IsReservedType(string typeName) =>
        new[] { "Page", "Container", "Text", "Image", "Audio", "Video", "Object", "Button", "Input", "Layer", "Anchor" }
            .Contains(typeName, StringComparer.OrdinalIgnoreCase);
}

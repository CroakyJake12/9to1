namespace Haven.UI;

public enum HavenAccessibleRole
{
    None,
    Window,
    Group,
    Text,
    Button,
    Input,
    CheckBox,
    Slider,
    List,
    ListItem,
    Tab,
    TabItem,
    Image,
    Link,
    Menu,
    MenuItem,
    Dialog
}

/// <summary>
/// Accessibility-surface metadata for a <see cref="HavenElement"/>. The public property surface is
/// preserved; each setter raises <see cref="Changed"/> when the value actually changes so the owning
/// element can invalidate the scene (including the automation peer composition) on role/name/exposure
/// mutations that otherwise have no <see cref="HavenInvalidationKinds"/> notification (NATIVE-007/010).
/// </summary>
public sealed class HavenAccessibility
{
    private HavenAccessibleRole _role;
    private string? _accessibleName;
    private string? _description;
    private bool _focusable;
    private bool _selected;
    private bool _checked;
    private bool _enabled = true;
    private bool _isPassword;

    /// <summary>Raised synchronously when any accessibility property changes to a new value.</summary>
    public event EventHandler? Changed;

    private void RaiseIfChanged<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public HavenAccessibleRole Role { get => _role; set => RaiseIfChanged(ref _role, value); }
    public string? AccessibleName { get => _accessibleName; set => RaiseIfChanged(ref _accessibleName, value); }
    public string? Description { get => _description; set => RaiseIfChanged(ref _description, value); }
    public bool Focusable { get => _focusable; set => RaiseIfChanged(ref _focusable, value); }
    public bool Selected { get => _selected; set => RaiseIfChanged(ref _selected, value); }
    public bool Checked { get => _checked; set => RaiseIfChanged(ref _checked, value); }
    public bool Enabled { get => _enabled; set => RaiseIfChanged(ref _enabled, value); }
    public bool IsPassword { get => _isPassword; set => RaiseIfChanged(ref _isPassword, value); }
}

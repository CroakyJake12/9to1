using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Adapter from the legacy Haven input to the typed composer input port. This is a transport
/// adapter only; it does not create a second draft owner or alter text semantics.
/// </summary>
internal sealed class HavenChatComposerInputPort : IChatComposerInputPort
{
    private readonly Input _input;
    private readonly Action? _focus;
    public HavenChatComposerInputPort(Input input, Action? focus = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _focus = focus;
        _input.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        _input.Invalidated += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public string Text
    {
        get => _input.Text;
        set => _input.Text = value ?? string.Empty;
    }

    public int CaretIndex => _input.CaretIndex;
    public int SelectionStart => _input.SelectionStart;
    public int SelectionLength => _input.SelectionLength;
    public string SelectedText => _input.SelectedText;
    public bool IsFocused => (_input.State & HavenElementState.Focused) == HavenElementState.Focused;

    public event EventHandler? TextChanged;
    public event EventHandler? Invalidated;

    public void PlaceCaretAtEnd(bool extendSelection = false) => _input.PlaceCaretAtEnd(extendSelection);
    public void SetSelection(int anchor, int caret) => _input.SetSelection(anchor, caret);
    public void ClearSelection() => _input.ClearSelection();
    public void Focus() => _focus?.Invoke();
}
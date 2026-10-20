namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Typed presentation-independent composer input port. The Chat engine depends on this
/// contract rather than a Haven or CUI control; a concrete adapter may wrap either the
/// legacy Haven input or the canonical CUI input without changing engine behavior.
/// </summary>
public interface IChatComposerInputPort
{
    string Text { get; set; }
    int CaretIndex { get; }
    int SelectionStart { get; }
    int SelectionLength { get; }
    string SelectedText { get; }
    bool IsFocused { get; }

    event EventHandler? TextChanged;
    event EventHandler? Invalidated;

    void PlaceCaretAtEnd(bool extendSelection = false);
    void SetSelection(int anchor, int caret);
    void ClearSelection();
    void Focus();
}
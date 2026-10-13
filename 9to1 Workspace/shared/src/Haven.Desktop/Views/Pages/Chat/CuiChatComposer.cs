using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Existing composer operations over the one real canonical CUI text input.</summary>
internal sealed class CuiChatComposer : IDisposable
{
    internal TextBox Control { get; }
    public CuiChatComposer(TextBox input)
    {
        Control = input;
        input.PropertyChanged += OnPropertyChanged;
    }
    public string Text { get => Control.Text ?? ""; set => Control.Text = value; }
    public int CaretIndex => Control.CaretIndex;
    public event EventHandler? Invalidated;
    public void PlaceCaretAtEnd() => SetSelection(Text.Length, Text.Length);
    public void SetSelection(int start, int end)
    {
        Control.SelectionStart = Math.Clamp(start, 0, Text.Length);
        Control.SelectionEnd = Math.Clamp(end, 0, Text.Length);
        Control.CaretIndex = Control.SelectionEnd;
    }
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty || args.Property == TextBox.CaretIndexProperty)
            Invalidated?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() => Control.PropertyChanged -= OnPropertyChanged;
}

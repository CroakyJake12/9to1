using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Controls;

public enum MarkdownCodeAction { Copy, AskToRun, AskToApply }
public sealed record MarkdownCodeActionRequest(MarkdownCodeAction Action, string Language, string Code);

/// <summary>Compatibility facade over the single maintained CUI Markdown renderer.
/// Existing Desktop control classes and public code-action API stay intact.</summary>
public sealed class ProductionMarkdownView : CuiMarkdownView
{
    public new static readonly StyledProperty<string> TextProperty = CuiMarkdownView.TextProperty.AddOwner<ProductionMarkdownView>();
    public new event Action<MarkdownCodeActionRequest>? CodeActionRequested;

    public ProductionMarkdownView()
    {
        // Preserve the existing Desktop clipboard/event behavior. Its historical
        // asynchronous event handler has no owning host close contract; this
        // compatibility adapter does not claim to qualify that old lifetime.
        // The shared renderer itself performs no asynchronous effect.
        base.CodeActionRequested += async request =>
        {
            if (request.Action == CuiMarkdownCodeAction.Copy)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null) await clipboard.SetTextAsync(request.Code);
            }
            CodeActionRequested?.Invoke(new((MarkdownCodeAction)request.Action, request.Language, request.Code));
        };
    }

    protected override Border CreateSurface() => new HavenAdaptiveSurface();
    protected override TextBox CreateCodeInput() => new HavenTextInput();
    protected override Button CreateCodeActionButton() => new HavenButton();
    protected override CheckBox CreateTaskCheckbox() => new HavenCheckBox();
}

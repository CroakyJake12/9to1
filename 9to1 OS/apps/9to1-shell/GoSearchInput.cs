using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CakeOS.Cui;

namespace NineToOne.Os.Shell;

/// <summary>Single-line keyboard submission through the same owning Go action as the Search button.</summary>
public sealed class GoSearchInput(ICuiActionDispatcher actions) : TextBox
{
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _submission;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { _lifetime = new(); base.OnAttachedToVisualTree(e); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var lifetime = _lifetime; _lifetime = null;
        _submission?.Cancel(); lifetime?.Cancel(); lifetime?.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); // Preserve text editing and handled platform input before considering submission.
        if (e.Handled || e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || AcceptsReturn || _lifetime is not { } lifetime) return;
        e.Handled = true;
        _submission?.Cancel();
        var submitted = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _submission = submitted;
        _ = SubmitAsync(submitted);
    }
    private async Task SubmitAsync(CancellationTokenSource submitted)
    {
        try { await actions.DispatchAsync("Search", null, submitted.Token); }
        catch (OperationCanceledException) when (submitted.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if (ReferenceEquals(_submission, submitted) && !submitted.IsCancellationRequested)
                ToolTip.SetTip(this, "Go search could not be submitted. Use Search or reopen Go.");
        }
        finally
        {
            if (ReferenceEquals(_submission, submitted)) _submission = null;
            submitted.Dispose();
        }
    }
}

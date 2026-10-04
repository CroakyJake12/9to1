namespace CakeOS.Cui.Runtime;

/// <summary>Safe UI outcome; private dispatcher exceptions and arguments never become user-facing diagnostics.</summary>
public sealed class CuiActionFailure(string code, string message, bool cancelled) : EventArgs
{
    public string Code { get; } = code;
    public string Message { get; } = message;
    public bool Cancelled { get; } = cancelled;
}

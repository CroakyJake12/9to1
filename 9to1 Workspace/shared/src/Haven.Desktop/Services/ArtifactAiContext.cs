using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Services;

/// <summary>Captures the complete authorised loaded canonical model on its UI thread, independent of viewport.</summary>
internal sealed class ArtifactAiContext(string appId, Func<(string? Id, object? Model)> artifact) : IAppAiContext, IAppAiActions
{
    public IReadOnlyList<AppAiActionDescriptor> Actions => [];

    public async ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capture = Dispatcher.UIThread.CheckAccess() ? Capture() : await Dispatcher.UIThread.InvokeAsync(Capture);
        cancellationToken.ThrowIfCancellationRequested();
        return capture;
    }

    private AppAiContextSnapshot Capture()
    {
        var (id, model) = artifact();
        var payload = JsonSerializer.SerializeToElement(model, model?.GetType() ?? typeof(object));
        var revision = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload.GetRawText())));
        return new(appId, id is null ? "launch" : "editor", id,
            id is null ? appId + " launch surface" : "Full canonical " + appId + " artifact",
            null, new Dictionary<string, JsonElement> { ["artifact"] = payload }, AppAiDataSensitivity.Private,
            DateTimeOffset.UtcNow, revision, [], "Read-only semantic adapter; mutation APIs not yet registered");
    }

    public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AppAiActionResult.Rejected("This adapter has no registered mutation action.", "unknown-action"));
}

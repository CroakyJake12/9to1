using Avalonia;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private HomeDashboardModelSource? _modelSource;
    private HomeDashboardModels? _modelSnapshot;
    private HomeHuggingFaceBrowser? _modelBrowser;
    private CuiContainerControl? _modelBrowserHost;
    private CancellationTokenSource? _modelRefreshCancellation;
    private Task? _modelRefreshTask;
    private int _modelColumns = 1;

    public void EnableDeviceModelRecommendations(HomeDashboardModelSource source)
    {
        if (_modelSource is not null) throw new InvalidOperationException("The dashboard model owner is already configured.");
        _modelSource = source;
        _modelBrowser = new HomeHuggingFaceBrowser(source.Catalogue, source.Profile, RunHomeWorkAsync);
        _modelBrowserHost!.AddChild(_modelBrowser);
        _dashboardBindings.Set("HasModelSource", true);
    }

    private void ConfigureModelBindings()
    {
        _dashboardBindings.Set("HasModelSource", false);
        _dashboardBindings.Set("IsReviewingModel", false);
        _dashboardBindings.Set("DeviceSummary", "Checking this device…");
        _dashboardBindings.Set("DeviceMemory", "Device details stay on this device.");
        _dashboardBindings.Set("ModelStatus", "Loading recommendations…");
        _dashboardBindings.Set("ModelColumns", "1fr");
        _dashboardBindings.Set("RecommendedModels", Array.Empty<Dictionary<string, object?>>());
        SizeChanged += (_, _) =>
        {
            var columns = Bounds.Width >= 1000 ? 3 : Bounds.Width >= 680 ? 2 : 1;
            if (columns == _modelColumns) return;
            _modelColumns = columns;
            PublishRecommendedModelRows();
        };
    }

    private Task RefreshDeviceModelsAsync(CancellationToken token)
    {
        if (_modelSource is null || _disposed || !_dashboardActive) return Task.CompletedTask;
        if (_modelRefreshTask is { IsCompleted: false } previous)
            return _modelRefreshCancellation?.IsCancellationRequested == true
                ? RestartModelRefreshAsync(previous, token) : previous;
        return _modelRefreshTask = ReadDeviceModelsAsync(token);
    }

    private async Task RestartModelRefreshAsync(Task previous, CancellationToken token)
    {
        await previous;
        token.ThrowIfCancellationRequested();
        await RefreshDeviceModelsAsync(token);
    }

    private async Task ReadDeviceModelsAsync(CancellationToken token)
    {
        _modelRefreshCancellation?.Dispose();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _modelRefreshCancellation = cancellation;
        var generation = _activationGeneration;
        _modelSnapshot = null;
        PublishRecommendedModelRows();
        _dashboardBindings.Set("ModelStatus", "Checking available memory and Hugging Face model files…");
        try
        {
            var snapshot = await AwaitHomeSourceAsync(_modelSource!.ReadAsync(cancellation.Token));
            if (cancellation.IsCancellationRequested || _disposed || !_dashboardActive || generation != _activationGeneration) return;
            _modelSnapshot = snapshot;
            var hardware = snapshot.Hardware;
            var gpu = hardware.Gpus.Count == 0 ? "GPU memory unavailable" : string.Join(" · ", hardware.Gpus.Select(item =>
                item.Name + (item.TotalBytes is { } bytes ? " · " + LocalModelMemoryEstimator.FormatBytes(bytes) + " VRAM" : " · VRAM unknown")));
            _dashboardBindings.Set("DeviceSummary", $"{hardware.Architecture} · {hardware.LogicalProcessors} logical processors · {gpu}");
            var memory = hardware.TotalRamBytes is { } total ? LocalModelMemoryEstimator.FormatBytes(total) + " RAM" : "RAM unknown";
            memory += hardware.AvailableRamBytes is { } available ? " · " + LocalModelMemoryEstimator.FormatBytes(available) + " available" : " · available RAM unknown";
            _dashboardBindings.Set("DeviceMemory", memory + $" · measured {hardware.ObservedAt.ToLocalTime():t}. " + hardware.Limitation);
            _dashboardBindings.Set("ModelStatus", snapshot.Status);
            PublishRecommendedModelRows();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && _dashboardActive && generation == _activationGeneration)
                _dashboardBindings.Set("ModelStatus", "Recommendations are unavailable. Retry, or browse all models in Discover.");
        }
        finally
        {
            if (ReferenceEquals(_modelRefreshCancellation, cancellation)) _modelRefreshCancellation = null;
        }
    }

    private void PublishRecommendedModelRows()
    {
        _dashboardBindings.Set("ModelColumns", string.Join(" ", Enumerable.Repeat("1fr", _modelColumns)));
        _dashboardBindings.Set("RecommendedModels", (_modelSnapshot?.Models ?? []).Select((model, index) =>
            new Dictionary<string, object?>
            {
                ["Key"] = model.Key, ["Name"] = model.Name, ["Publisher"] = model.Publisher,
                ["Format"] = model.Format, ["DownloadSize"] = model.DownloadSize,
                ["FitLabel"] = model.FitLabel, ["Explanation"] = model.Fit.Explanation,
                ["Row"] = index / _modelColumns, ["Column"] = index % _modelColumns
            }).ToArray());
    }

    private async Task ReviewModelAsync(string key, CancellationToken token)
    {
        if (_modelSnapshot is not { } snapshot || _modelBrowser is null || _modelSource is null) return;
        var model = snapshot.Models.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.Key, key));
        if (model is null) return;
        if (!Equals(snapshot.Actor, await _modelSource.Profile(token)))
        {
            _modelSnapshot = null; PublishRecommendedModelRows();
            _dashboardBindings.Set("ModelStatus", "The active profile changed. Refresh recommendations to continue.");
            return;
        }
        token.ThrowIfCancellationRequested();
        if (_disposed || !_dashboardActive) return;
        _dashboardBindings.Set("IsReviewingModel", true);
        await AwaitHomeSourceAsync(_modelBrowser.ReviewAsync(model, snapshot.Actor, token));
        if (!_disposed && _dashboardActive) _modelBrowser.BringIntoView();
    }

    private sealed class DashboardActions(NewDashboardPage owner) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner._disposed || owner._homeWork.IsRetiring) return ValueTask.CompletedTask;
            return command switch
            {
                "RefreshRecommendations" => new(owner.RunHomeWorkAsync(owner.RefreshDeviceModelsAsync, cancellationToken)),
                "ReviewRecommendedModel" when parameter is string key => new(owner.RunHomeWorkAsync(token => owner.ReviewModelAsync(key, token), cancellationToken)),
                "CloseModelReview" => CloseReview(),
                _ => ValueTask.FromException(new InvalidOperationException("Unknown dashboard action."))
            };
        }
        private ValueTask CloseReview()
        {
            owner._modelBrowser?.Deactivate();
            owner._dashboardBindings.Set("IsReviewingModel", false);
            return ValueTask.CompletedTask;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Services;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.ViewModels;
using HavenOS.Home.Discover;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Home's provider catalogue view, backed by the registered model providers.</summary>
public sealed class HomeDiscoverPage : UserControl, IActivatablePage, IDisposable
{
    private readonly HomeDiscoverCatalog _catalog;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly CuiControlLoader _loader = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new() { ItemsSource = new[] { "All categories", "Chat", "Image", "Voice", "Audio (advanced)", "Video (advanced)" }, SelectedIndex = 0 };
    private readonly ComboBox _locality = new() { ItemsSource = new[] { "All locations", "Local", "Cloud", "Remote" }, SelectedIndex = 0 };
    private readonly ListBox _models = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _status = new();
    private readonly TextBlock _details = new();
    private readonly Button _refresh = new();
    private readonly Button _manage = new();
    private readonly Button _compare = new();
    private CancellationTokenSource? _readCancellation;
    private long _generation;
    private bool _disposed;

    public HomeDiscoverPage(HomeDiscoverCatalog catalog)
    {
        _catalog = catalog;
        _work = new(StopDiscoverAsync, DetachDiscoverAsync);
        using var stream = typeof(HomeDiscoverPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Discover.cui")
            ?? throw new InvalidDataException("The Home Discover CUI document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Discover.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home Discover CUI document is invalid.");
        _loader.BindAuthoredControl("discover-search", _ => _search);
        _loader.BindAuthoredControl("discover-category", _ => _category);
        _loader.BindAuthoredControl("discover-locality", _ => _locality);
        _loader.BindAuthoredControl("discover-models", _ => _models);
        _loader.BindAuthoredControl("discover-status", _ => _status);
        _loader.BindAuthoredControl("discover-details", _ => _details);
        _loader.BindAuthoredControl("discover-refresh", _ => _refresh);
        _loader.BindAuthoredControl("discover-manage", _ => _manage);
        _loader.BindAuthoredControl("discover-compare", _ => _compare);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Home Discover could not be rendered: " + string.Join("; ", loaded.Diagnostics.Select(item => item.Message)));
        Content = loaded.Root;
        _models.ItemTemplate = new FuncDataTemplate<HomeDiscoverModel>((model, _) => new TextBlock
        {
            Text = model is null ? string.Empty : $"{model.DisplayName ?? model.Name} · {model.Identity.ProviderId} · {model.Locality}",
            TextWrapping = TextWrapping.Wrap
        });
        _models.SelectionChanged += (_, _) =>
        {
            var selected = SelectedModels();
            SetDiscoverValue(_compare, "IsEnabled", selected.Length is > 0 and <= 20);
            SetDiscoverValue(_details, "Text", selected.Length > 0 ? string.Join("\n\n", selected.Select(Describe)) : "Select a model to inspect its reported metadata.");
        };
        _refresh.Click += async (_, _) => await RunDiscoverEventAsync(() => ActivateAsync(CancellationToken.None));
        _manage.Click += (_, _) => { if (!_work.IsRetiring) ManageProvidersRequested?.Invoke(this, EventArgs.Empty); };
        _compare.Click += async (_, _) => await RunDiscoverEventAsync(CompareSelectedAsync);
        _search.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            await RunDiscoverEventAsync(() => ActivateAsync(CancellationToken.None));
        };
    }

    public event EventHandler? ManageProvidersRequested;

    private async Task RunDiscoverEventAsync(Func<Task> body)
    {
        try { await body(); }
        catch (Exception error)
        {
            // The original work owner keeps source/operation failures for its external close.
            // UI events report them without escaping through async-void on tab retirement.
            if (!_disposed && !_work.IsRetiring) SetDiscoverValue(_status, "Text", error.Message);
        }
    }

    public Task ActivateAsync(CancellationToken cancellationToken) => RunDiscoverAsync(ActivateCoreAsync, cancellationToken);

    private async Task ActivateCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        var selected = SelectedModels().Select(model => model.Identity.StableKey).ToHashSet(StringComparer.Ordinal);
        var (generation, token) = StartRead(cancellationToken);
        _models.ItemsSource = Array.Empty<HomeDiscoverModel>();
        SetDiscoverValue(_details, "Text", "Reading configured providers…");
        SetDiscoverValue(_status, "Text", "Reading configured providers…");
        var category = _category.SelectedIndex > 0 ? (HomeDiscoverCategory?)(_category.SelectedIndex - 1) : null;
        var locality = _locality.SelectedIndex > 0 ? (HomeDiscoverLocality?)_locality.SelectedIndex : null;
        try
        {
            var page = await AwaitDiscoverSourceAsync(_catalog.SearchAsync(new(_search.Text, category, locality, PageSize: 200), token));
            if (!Current(generation, token)) return;
            _models.ItemsSource = page.Models;
            foreach (var model in page.Models.Where(model => selected.Contains(model.Identity.StableKey)))
                _models.SelectedItems?.Add(model);
            var problems = page.Providers.Where(provider => provider.ErrorMessage is not null)
                .Select(provider => $"{provider.DisplayName}: {provider.ErrorMessage}");
            SetDiscoverValue(_status, "Text", $"{page.State} · {page.Models.Count} of {page.TotalCount} models · {page.Providers.Count} configured providers" +
                (page.NextPageToken is null ? string.Empty : ". Narrow your search to see more results.") +
                string.Concat(problems.Select(problem => "\n" + problem)));
            if (SelectedModels().Length == 0) SetDiscoverValue(_details, "Text", "Select models to compare. Only capabilities reported by providers are shown.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (Current(generation, token)) SetDiscoverValue(_status, "Text", "Model discovery failed. Refresh to query the configured providers again.");
        }
    }

    private Task CompareSelectedAsync() => RunDiscoverAsync(CompareSelectedCoreAsync, CancellationToken.None);

    private async Task CompareSelectedCoreAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedModels();
        if (selected.Length is < 1 or > 20) return;
        var (generation, token) = StartRead(cancellationToken);
        SetDiscoverValue(_details, "Text", "Refreshing selected models…");
        try
        {
            var compared = await AwaitDiscoverSourceAsync(_catalog.CompareAsync(selected.Select(model => model.Identity).ToArray(), token));
            if (!Current(generation, token)) return;
            if (!SelectedModels().Select(model => model.Identity.StableKey)
                    .SequenceEqual(selected.Select(model => model.Identity.StableKey), StringComparer.Ordinal)) return;
            var details = string.Join("\n\n", compared.Models.Select(Describe));
            if (compared.MissingIdentities.Count > 0)
                details += $"\n{compared.MissingIdentities.Count} selected models are no longer available to this catalogue.";
            SetDiscoverValue(_details, "Text", details);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (Current(generation, token)) SetDiscoverValue(_details, "Text", "The model comparison could not be refreshed. Search again before choosing a model.");
        }
    }

    private HomeDiscoverModel[] SelectedModels() => _models.SelectedItems?.OfType<HomeDiscoverModel>().ToArray() ?? [];
    private static void SetDiscoverValue(Control control, string property, object? value) =>
        CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Home Discover state", order: 1);
    private (long Generation, CancellationToken Token) StartRead(CancellationToken cancellationToken)
    {
        _readCancellation?.Cancel();
        _readCancellation?.Dispose();
        _readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return (++_generation, _readCancellation.Token);
    }
    private bool Current(long generation, CancellationToken token) => !_disposed && !_work.IsRetiring && !token.IsCancellationRequested && generation == _generation;
    private static string Describe(HomeDiscoverModel model) =>
        $"{model.DisplayName ?? model.Name} · {model.Identity.ProviderId}\n" +
        $"Model ID: {model.Identity.ModelId} · {model.Locality} · {model.ConnectionState}\n" +
        $"Architecture: {model.Architecture ?? "Unknown"} · Parameters: {model.ParameterInformation ?? "Unknown"} · Format: {model.QuantizationOrFormat ?? "Unknown"}\n" +
        $"Context: {model.ContextLimit?.ToString() ?? "Unknown"} · Cost: {model.CostClass ?? "Unknown"} · Residency: {model.Residency ?? "Unknown"}\n" +
        $"Capabilities: {(model.Capabilities is { Count: > 0 } ? string.Join(", ", model.Capabilities.Order()) : "Unknown")}";
    public void Deactivate() { _generation++; _readCancellation?.Cancel(); }
    public Task? OriginalClose => _work.OriginalClose;
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
    public Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();
    public void Dispose() => _work.RequestRetirement();

    private Task<T> AwaitDiscoverSourceAsync<T>(Task<T> actual) =>
        (_work.Executing ?? throw new InvalidOperationException("Discover source work requires its owning operation.")).AwaitAsync(actual);

    private Task RunDiscoverAsync(Func<CancellationToken, Task> body, CancellationToken token) => _work.RunAsync(async original =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
        Task? source = null;
        await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => { source = body(linked.Token); }).GetTask()).ConfigureAwait(false);
        await original.AwaitAsync(source ?? throw new InvalidOperationException("The Discover read was not acquired.")).ConfigureAwait(false);
    });
    private async Task StopDiscoverAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { _disposed = true; Deactivate(); });
    }
    private async Task DetachDiscoverAsync()
    {
        Task? actions = null;
        await Dispatcher.UIThread.InvokeAsync(() => { _loader.Dispose(); actions = _loader.WhenActionsIdleAsync(); });
        if (actions is not null) await actions.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => { _readCancellation?.Dispose(); Content = null; });
    }
}

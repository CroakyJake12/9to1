using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Data;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;

namespace HavenOS.Apps.Data.Native;

/// <summary>Standalone owner of the maintained local workbook component.
/// This host does not certify or replace the separate Calc/UNO/Data.Cui workflow.</summary>
public sealed class DataAppHost : IAsyncDisposable
{
    private readonly HavenEventBus _events;
    private readonly IDataWorkbookRepository _repository;
    private readonly Button _landingNew;
    private readonly Button _originalNew;
    private Task? _initialize;
    private Task<bool>? _closePreparation;
    private bool _disposed;

    private DataAppHost(IDataWorkbookRepository repository, IDataWorkbookFormatService formats,
        IDataWorkbookQueryService queries)
    {
        Dispatcher.UIThread.VerifyAccess();
        _repository = repository;
        _events = new();
        DataPage? created = null;
        try
        {
            Page = created = new(_events, repository, formats, queries);
            var root = ((HavenSceneControl)Page.Content!).Root
                ?? throw new InvalidOperationException("The maintained Data editor did not provide its original scene.");
            _landingNew = root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "Data.Landing.New");
            _originalNew = root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "Data.Workbook.New");
            // The landing's existing Button has no action subscription. Forward
            // this actual native intent to the SAME maintained create action;
            // no sample workbook or alternative persistence is synthesized.
            _landingNew.Invoked += OnLandingNew;
        }
        catch (Exception sourceFailure)
        {
            var failures = new List<Exception> { sourceFailure };
            if (created is not null)
                try { created.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { _events.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Data local component construction retained source and cleanup failures.", failures);
        }
    }

    public DataPage Page { get; }
    public DataWorkbook? Workbook => Page.Workbook;
    public bool IsDisposed => _disposed;

    public static DataAppHost CreateDefault() => Create(new DataWorkbookRepository(new AppPaths()),
        new DataXlsxFormatService(), new DataWorkbookQueryService());

    public static DataAppHost Create(IDataWorkbookRepository repository,
        IDataWorkbookFormatService formats, IDataWorkbookQueryService queries)
    {
        ArgumentNullException.ThrowIfNull(repository); ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(queries);
        return new(repository, formats, queries);
    }

    public Task InitializeAsync(CancellationToken token = default)
    {
        DemandAlive();
        return _initialize ??= InitializeCoreAsync(token);
    }

    private async Task InitializeCoreAsync(CancellationToken token)
    {
        // Preserve a real repository read error instead of interpreting the
        // page's legacy status-only initialization refusal as an empty store.
        var before = await _repository.ListAsync(token);
        await Page.InitializeAsync(token);
        DemandAlive();
        if (before.Count != 0 && Page.Workbook is null)
            throw new InvalidOperationException("The existing local workbook could not be published by Data.");
    }

    public async Task<bool> OpenWorkbookAsync(Guid id, CancellationToken token = default)
    {
        DemandAlive(); await InitializeAsync(token); DemandAlive();
        return await Page.OpenWorkbookAsync(id, token);
    }

    public Task<bool> TrySaveBeforeCloseAsync(CancellationToken token = default)
    {
        DemandAlive();
        if (_closePreparation is { IsCompleted: false }) return _closePreparation;
        return _closePreparation = PrepareCloseAsync(token);
    }

    private async Task<bool> PrepareCloseAsync(CancellationToken token)
    {
        if (_initialize is not null) await _initialize;
        DemandAlive(); token.ThrowIfCancellationRequested();
        // This is the original page's UI availability observation, not a grant
        // or proof that all its legacy async-void source callbacks have drained.
        if (!_originalNew.GetValue(HavenProperties.Enabled)) return false;
        Page.IsEnabled = false;
        try { return await Page.SaveAsync("Save before closing Data", token); }
        finally { if (!_disposed) Page.IsEnabled = true; }
    }

    private void OnLandingNew(object? sender, EventArgs args)
    {
        DemandAlive();
        if (!Page.IsEnabled || !_originalNew.GetValue(HavenProperties.Enabled)) return;
        var originalIntent = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        _originalNew.KeyDown(originalIntent); _originalNew.KeyUp(originalIntent);
    }

    private void DemandAlive()
    {
        Dispatcher.UIThread.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        if (_initialize is { IsFaulted: true } or { IsCanceled: true })
        {
            Exception? sourceFailure = null;
            try { await _initialize; } catch (Exception error) { sourceFailure = error; }
            if (Workbook is not null || Page.IsDirty)
                throw new AggregateException("Data initialization failed with a retained workbook; keep its editor open.", sourceFailure!);
            RetireOriginalComponents(sourceFailure);
            return;
        }
        if (!await TrySaveBeforeCloseAsync())
            throw new InvalidOperationException("Data is busy or has an unsaved workbook. Keep the original editor open.");
        RetireOriginalComponents(null);
    }

    private void RetireOriginalComponents(Exception? sourceFailure)
    {
        _disposed = true;
        var failures = new List<Exception>();
        if (sourceFailure is not null) failures.Add(sourceFailure);
        try { _landingNew.Invoked -= OnLandingNew; Page.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        try { _events.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Data local component cleanup retained failures.", failures);
    }
}

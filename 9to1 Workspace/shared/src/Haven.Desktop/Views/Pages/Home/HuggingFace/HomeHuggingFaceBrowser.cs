using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Hugging Face CUI panel whose work is retained by the containing Discover page.</summary>
public sealed class HomeHuggingFaceBrowser : UserControl, IDisposable
{
    private readonly IHuggingFaceModelService? _service;
    private readonly Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>>? _profile;
    private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _runOwned;
    private readonly CuiControlLoader _loader = new();
    private readonly TextBox _query = new();
    private readonly ListBox _repositories = new();
    private readonly TextBlock _repositoryDetails = new();
    private readonly ListBox _files = new();
    private readonly TextBlock _installStatus = new();
    private readonly ProgressBar _installProgress = new() { Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly TextBox _token = new() { PasswordChar = '•', RevealPassword = false };
    private readonly TextBlock _tokenStatus = new();
    private readonly TextBlock _browserStatus = new();
    private readonly Button _searchButton = new();
    private readonly Button _installButton = new();
    private readonly Button _cancelInstallButton = new();
    private readonly Button _saveTokenButton = new();
    private readonly Button _clearTokenButton = new();
    private readonly Button _removeTokenButton = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _installCancellation;
    private HuggingFaceModelDetails? _selectedDetails;
    private HuggingFaceModelFile? _selectedFile;
    private AuthenticatedResourceActor? _boundActor;
    private long _generation;
    private bool _hasToken;
    private bool _busy;
    private bool _disposed;

    public HomeHuggingFaceBrowser(IHuggingFaceModelService? service,
        Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>>? profile,
        Func<Func<CancellationToken, Task>, CancellationToken, Task> runOwned)
    {
        _service = service;
        _profile = profile;
        _runOwned = runOwned ?? throw new ArgumentNullException(nameof(runOwned));
        using var stream = typeof(HomeHuggingFaceBrowser).Assembly.GetManifestResourceStream("Haven.Desktop.Home.HuggingFace.cui")
            ?? throw new InvalidDataException("The Hugging Face CUI document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "HomeHuggingFace.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Hugging Face CUI document is invalid.");

        _loader.BindAuthoredControl("hf-query", _ => _query);
        _loader.BindAuthoredControl("hf-repositories", _ => _repositories);
        _loader.BindAuthoredControl("hf-repository-details", _ => _repositoryDetails);
        _loader.BindAuthoredControl("hf-files", _ => _files);
        _loader.BindAuthoredControl("hf-install-status", _ => _installStatus);
        _loader.BindAuthoredControl("hf-install-progress", _ => _installProgress);
        _loader.BindAuthoredControl("hf-token", _ => _token);
        _loader.BindAuthoredControl("hf-token-status", _ => _tokenStatus);
        _loader.BindAuthoredControl("hf-browser-status", _ => _browserStatus);
        _loader.BindAuthoredControl("hf-search", _ => _searchButton);
        _loader.BindAuthoredControl("hf-install", _ => _installButton);
        _loader.BindAuthoredControl("hf-cancel-install", _ => _cancelInstallButton);
        _loader.BindAuthoredControl("hf-token-save", _ => _saveTokenButton);
        _loader.BindAuthoredControl("hf-token-clear", _ => _clearTokenButton);
        _loader.BindAuthoredControl("hf-token-remove", _ => _removeTokenButton);
        var loaded = _loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Hugging Face browser could not be rendered: " +
                string.Join("; ", loaded.Diagnostics.Select(item => item.Message)));
        Content = loaded.Root;

        _repositories.ItemTemplate = new FuncDataTemplate<HuggingFaceModelSummary>((model, _) => new TextBlock
        {
            Text = model is null ? string.Empty : $"{model.RepositoryId} · {model.Downloads:N0} downloads · {model.Likes:N0} likes · {(model.Gated ? "Gated" : "Public")}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _files.ItemTemplate = new FuncDataTemplate<HuggingFaceModelFile>((file, _) => new TextBlock
        {
            Text = file is null ? string.Empty : $"{file.Path} · {FormatBytes(file.Size)} · SHA-256 {file.Sha256[..12]}…",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _query.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            await DispatchOwnedAsync(SearchCoreAsync);
        };
        _searchButton.Click += async (_, _) => await DispatchOwnedAsync(SearchCoreAsync);
        _repositories.SelectionChanged += async (_, _) =>
        {
            if (_repositories.SelectedItem is HuggingFaceModelSummary item)
                await DispatchOwnedAsync(token => LoadDetailsCoreAsync(item, token));
        };
        _files.SelectionChanged += (_, _) =>
        {
            _selectedFile = _files.SelectedItem as HuggingFaceModelFile;
            UpdateInstallAvailability();
            if (_selectedFile is { } file)
                SetText(_browserStatus, $"Exact file selected: {file.Path} · {FormatBytes(file.Size)} · SHA-256 {file.Sha256}");
        };
        _installButton.Click += async (_, _) => await DispatchOwnedAsync(InstallCoreAsync);
        _cancelInstallButton.Click += (_, _) => CancelInstall();
        _saveTokenButton.Click += async (_, _) => await DispatchOwnedAsync(SaveTokenCoreAsync);
        _clearTokenButton.Click += (_, _) => ClearTokenInput("Unsaved token input cleared.");
        _removeTokenButton.Click += async (_, _) => await DispatchOwnedAsync(RemoveTokenCoreAsync);
        SetText(_browserStatus, _service is null || _profile is null
            ? "Hugging Face browsing is unavailable in this Home session."
            : "Search is public; installation requires verified repository metadata.");
        UpdateInstallAvailability();
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        if (!ReadyForHuggingFace()) { SetText(_tokenStatus, "Secure Hugging Face access is unavailable in this Home session."); return; }
        var (generation, token) = BeginOperation(cancellationToken);
        try
        {
            var actor = await CurrentActorAsync(token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Search again to continue.");
                _boundActor = actor;
            }
            var hasToken = await _service!.HasTokenAsync(actor.ProfileId, token);
            if (!await IsCurrentActorAsync(actor, token)) { ClearProfileState("The active profile changed. Reopen Discover to continue."); return; }
            if (IsCurrent(generation, token))
            {
                _hasToken = hasToken;
                SetText(_tokenStatus, hasToken ? "A token is saved securely for this profile." : "No token is saved. Public search remains available.");
                UpdateInstallAvailability();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (IsCurrent(generation, token)) ClearProfileState("Secure token status could not be checked."); }
        finally { EndOperation(generation); }
    }

    public void Deactivate()
    {
        _generation++;
        CancelAndDispose(ref _operationCancellation);
        CancelAndDispose(ref _installCancellation);
        _boundActor = null;
        _hasToken = false;
        _repositories.ItemsSource = Array.Empty<HuggingFaceModelSummary>();
        ClearSelectedRepository();
        ClearTokenInput();
        _busy = false;
        _installProgress.IsVisible = false;
        _installProgress.Value = 0;
        SetText(_installStatus, "No installation is running.");
        SetText(_tokenStatus, "Profile-bound token status is unavailable.");
        SetText(_browserStatus, "Search is public; installation requires verified repository metadata.");
        SetEnabled(_cancelInstallButton, false);
        UpdateInstallAvailability();
    }

    private async Task SearchCoreAsync(CancellationToken cancellationToken)
    {
        if (!ReadyForHuggingFace()) return;
        var query = _query.Text?.Trim() ?? string.Empty;
        if (query.Length == 0) { SetText(_browserStatus, "Enter a repository search phrase."); return; }
        if (query.Length > 200) { SetText(_browserStatus, "Search phrases can be at most 200 characters."); return; }
        var (generation, token) = BeginOperation(cancellationToken);
        _repositories.ItemsSource = Array.Empty<HuggingFaceModelSummary>();
        ClearSelectedRepository();
        SetText(_browserStatus, "Searching public Hugging Face GGUF repositories…");
        try
        {
            var actor = await CurrentActorAsync(token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Search again to continue.");
                _boundActor = actor;
            }
            var result = await _service!.SearchAsync(actor.ProfileId, query, token);
            if (!await IsCurrentActorAsync(actor, token)) { ClearProfileState("The active profile changed. Search again to continue."); return; }
            if (!IsCurrent(generation, token)) return;
            _repositories.ItemsSource = result;
            SetText(_browserStatus, result.Count == 0 ? "No GGUF repositories matched this public search." :
                $"Found {result.Count} GGUF repositories. Select one to inspect its exact files and license.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (IsCurrent(generation, token)) SetText(_browserStatus, SafeMessage(error, "Hugging Face search is temporarily unavailable.")); }
        finally { EndOperation(generation); }
    }

    private async Task LoadDetailsCoreAsync(HuggingFaceModelSummary repository, CancellationToken cancellationToken)
    {
        if (!ReadyForHuggingFace()) return;
        var (generation, token) = BeginOperation(cancellationToken);
        ClearSelectedRepository();
        SetText(_repositoryDetails, $"Loading verified GGUF files for {repository.RepositoryId}…");
        try
        {
            var actor = await CurrentActorAsync(token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Search again before choosing a repository.");
                _boundActor = actor;
                return;
            }
            var details = await _service!.GetDetailsAsync(actor.ProfileId, repository.RepositoryId, token);
            if (!await IsCurrentActorAsync(actor, token)) { ClearProfileState("The active profile changed. Search again to continue."); return; }
            if (!IsCurrent(generation, token)) return;
            _selectedDetails = details;
            _files.ItemsSource = details.Files;
            SetText(_repositoryDetails, $"{details.RepositoryId} · commit {ShortCommit(details.CommitSha)}\nLicense: {details.License} · {(details.Gated ? "Gated: account approval is required" : "Public repository")}. {details.Files.Count} verified GGUF file(s). Gated access is not granted by 9to1.");
            SetText(_browserStatus, details.Gated && !_hasToken
                ? "This repository is gated. Save a token for an account that already has access before installing."
                : "Choose the exact GGUF file to install. File size and SHA-256 are verified before import.");
            UpdateInstallAvailability();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (IsCurrent(generation, token)) SetText(_repositoryDetails, SafeMessage(error, "Repository details could not be read.")); }
        finally { EndOperation(generation); }
    }

    private async Task SaveTokenCoreAsync(CancellationToken cancellationToken)
    {
        if (!ReadyForHuggingFace()) return;
        var value = _token.Text ?? string.Empty;
        ClearTokenInput();
        if (string.IsNullOrWhiteSpace(value)) { SetText(_tokenStatus, "Paste a token into the masked field before saving."); return; }
        var (generation, token) = BeginOperation(cancellationToken);
        SetText(_tokenStatus, "Saving token to secure storage…");
        try
        {
            var actor = await CurrentActorAsync(token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Reopen Discover before saving a token.");
                _boundActor = actor;
                return;
            }
            await RunActorBoundAsync(actor, generation, token,
                operationToken => _service!.SaveTokenAsync(actor.ProfileId, value.Trim(), operationToken));
            if (!await IsCurrentActorAsync(actor, token)) { ClearProfileState("The active profile changed. Reopen Discover to continue."); return; }
            if (!IsCurrent(generation, token)) return;
            _hasToken = true;
            SetText(_tokenStatus, "Token saved securely for this profile. It cannot be displayed again.");
            SetText(_browserStatus, "Gated repositories can be installed only when this account already has access.");
            UpdateInstallAvailability();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (IsCurrent(generation, token)) SetText(_tokenStatus, "Could not save the token securely. The input was cleared."); }
        finally
        {
            if (generation == _generation) ClearTokenInput();
            EndOperation(generation);
        }
    }

    private async Task RemoveTokenCoreAsync(CancellationToken cancellationToken)
    {
        if (!ReadyForHuggingFace()) return;
        var (generation, token) = BeginOperation(cancellationToken);
        SetText(_tokenStatus, "Removing saved token…");
        try
        {
            var actor = await CurrentActorAsync(token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Reopen Discover before changing token access.");
                _boundActor = actor;
                return;
            }
            await RunActorBoundAsync(actor, generation, token,
                operationToken => _service!.RemoveTokenAsync(actor.ProfileId, operationToken));
            if (!await IsCurrentActorAsync(actor, token)) { ClearProfileState("The active profile changed. Reopen Discover to continue."); return; }
            if (!IsCurrent(generation, token)) return;
            _hasToken = false;
            SetText(_tokenStatus, "Saved token removed. Public search remains available.");
            UpdateInstallAvailability();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (IsCurrent(generation, token)) SetText(_tokenStatus, "Could not remove the saved token from secure storage."); }
        finally { EndOperation(generation); }
    }

    private async Task InstallCoreAsync(CancellationToken cancellationToken)
    {
        if (!ReadyForHuggingFace() || _selectedDetails is not { } details || _selectedFile is not { } file) return;
        var (generation, token) = BeginOperation(cancellationToken);
        var installCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _installCancellation = installCancellation;
        _busy = true;
        _installProgress.Value = 0;
        _installProgress.IsVisible = true;
        SetText(_installStatus, $"Preparing {file.Path} for verified installation…");
        SetEnabled(_cancelInstallButton, true);
        UpdateInstallAvailability();
        try
        {
            var actor = await CurrentActorAsync(installCancellation.Token);
            if (actor is null) { ClearProfileState("The active Home profile is unavailable."); return; }
            if (!Equals(_boundActor, actor))
            {
                ClearProfileState("The active profile changed. Search again before installing.");
                _boundActor = actor;
                return;
            }
            var hasToken = await _service!.HasTokenAsync(actor.ProfileId, installCancellation.Token);
            if (!await IsCurrentActorAsync(actor, installCancellation.Token))
            {
                ClearProfileState("The active profile changed. Installation was cancelled.");
                return;
            }
            if (!IsCurrent(generation, installCancellation.Token)) return;
            _hasToken = hasToken;
            SetText(_tokenStatus, hasToken ? "A token is saved securely for this profile." : "No token is saved. Public search remains available.");
            UpdateInstallAvailability();
            if (details.Gated && !hasToken)
            {
                SetText(_browserStatus, "This repository is gated. Save a token for an account that already has access before installing.");
                SetText(_installStatus, "Installation requires an authorized account token for this repository.");
                return;
            }
            var progress = new Progress<HuggingFaceInstallProgress>(value =>
            {
                if (_disposed || generation != _generation || installCancellation.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() => ApplyProgress(value, file, generation));
            });
            var result = await RunActorBoundAsync(actor, generation, installCancellation.Token,
                operationToken => _service!.InstallAsync(actor.ProfileId, details, file, progress, operationToken));
            if (!await IsCurrentActorAsync(actor, installCancellation.Token)) { ClearProfileState("The active profile changed. Reopen Discover to continue."); return; }
            if (!IsCurrent(generation, installCancellation.Token)) return;
            var observation = result.RuntimeObserved
                ? "Dulche observed the installed model; no inference was started."
                : "Awaiting Dulche engine observation; no inference was started.";
            var cleanup = result.CleanupPending ? " Temporary download cleanup is pending." : string.Empty;
            SetText(_installStatus, $"Installed {result.ModelId} from {result.RepositoryId}. {observation}{cleanup}");
            SetText(_browserStatus, $"Verified commit {ShortCommit(result.CommitSha)} · {result.FilePath} · {FormatBytes(result.Size)} · SHA-256 {result.Sha256}");
        }
        catch (OperationCanceledException) when (installCancellation.IsCancellationRequested)
        {
            if (IsCurrent(generation, CancellationToken.None)) SetText(_installStatus, "Installation cancelled.");
        }
        catch (Exception error)
        {
            if (IsCurrent(generation, CancellationToken.None)) SetText(_installStatus, SafeMessage(error, "The model could not be installed."));
        }
        finally
        {
            if (ReferenceEquals(_installCancellation, installCancellation))
            {
                _installCancellation = null;
                installCancellation.Dispose();
                _busy = false;
                SetEnabled(_cancelInstallButton, false);
                UpdateInstallAvailability();
            }
            EndOperation(generation);
        }
    }

    private void ApplyProgress(HuggingFaceInstallProgress progress, HuggingFaceModelFile file, long generation)
    {
        if (!_busy || !IsCurrent(generation, CancellationToken.None)) return;
        var phase = progress.Phase switch
        {
            HuggingFaceInstallPhase.Downloading => "Downloading",
            HuggingFaceInstallPhase.Validating => "Verifying file integrity",
            HuggingFaceInstallPhase.Importing => "Importing into Dulche",
            HuggingFaceInstallPhase.Installed => "Installed",
            _ => "Working"
        };
        if (progress.TotalBytes > 0)
        {
            _installProgress.Value = Math.Clamp((double)progress.ReceivedBytes / progress.TotalBytes, 0d, 1d) * 100d;
            SetText(_installStatus, $"{phase}: {FormatBytes(progress.ReceivedBytes)} of {FormatBytes(progress.TotalBytes)} · {file.Path}");
        }
        else
        {
            _installProgress.Value = 0;
            SetText(_installStatus, $"{phase}: {FormatBytes(progress.ReceivedBytes)} received · {file.Path}");
        }
    }

    private void CancelInstall()
    {
        if (_installCancellation is null || _installCancellation.IsCancellationRequested) return;
        SetText(_installStatus, "Cancelling installation…");
        _installCancellation.Cancel();
    }

    private async Task DispatchOwnedAsync(Func<CancellationToken, Task> operation)
    {
        if (_disposed) return;
        try { await _runOwned(operation, CancellationToken.None); }
        catch { if (!_disposed) SetText(_browserStatus, "The Hugging Face action could not be completed in the current Home session."); }
    }

    private async Task<AuthenticatedResourceActor?> CurrentActorAsync(CancellationToken token)
    {
        if (_profile is null) return null;
        token.ThrowIfCancellationRequested();
        return await _profile(token);
    }

    private async Task<bool> IsCurrentActorAsync(AuthenticatedResourceActor expected, CancellationToken token) =>
        Equals(await CurrentActorAsync(token), expected);

    private async Task<T> RunActorBoundAsync<T>(AuthenticatedResourceActor actor, long generation,
        CancellationToken cancellationToken, Func<CancellationToken, Task<T>> action)
    {
        using var changed = new CancellationTokenSource();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, changed.Token);
        using var monitorStop = new CancellationTokenSource();
        var monitor = MonitorActorAsync(actor, generation, cancellationToken, changed, monitorStop.Token);
        try { return await action(bound.Token); }
        finally
        {
            monitorStop.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
    }

    private async Task RunActorBoundAsync(AuthenticatedResourceActor actor, long generation,
        CancellationToken cancellationToken, Func<CancellationToken, Task> action) =>
        await RunActorBoundAsync(actor, generation, cancellationToken, async operationToken =>
        {
            await action(operationToken);
            return true;
        });

    private async Task MonitorActorAsync(AuthenticatedResourceActor expected, long generation,
        CancellationToken operationToken, CancellationTokenSource changed, CancellationToken stopToken)
    {
        try
        {
            while (!stopToken.IsCancellationRequested && !operationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), stopToken);
                if (Equals(await CurrentActorAsync(stopToken), expected)) continue;
                changed.Cancel();
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrent(generation, operationToken))
                        ClearProfileState("The active profile changed. The operation was cancelled.");
                });
                return;
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested || operationToken.IsCancellationRequested) { }
        catch
        {
            changed.Cancel();
            Dispatcher.UIThread.Post(() =>
            {
                if (IsCurrent(generation, operationToken))
                    ClearProfileState("The active profile could not be verified. The operation was cancelled.");
            });
        }
    }

    private bool ReadyForHuggingFace()
    {
        if (_disposed) return false;
        if (_service is not null && _profile is not null) return true;
        SetText(_browserStatus, "Hugging Face browsing is unavailable in this Home session.");
        return false;
    }

    private (long Generation, CancellationToken Token) BeginOperation(CancellationToken parent)
    {
        CancelAndDispose(ref _operationCancellation);
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
        return (++_generation, _operationCancellation.Token);
    }

    private void EndOperation(long generation)
    {
        if (generation == _generation) CancelAndDispose(ref _operationCancellation);
    }

    private bool IsCurrent(long generation, CancellationToken token) => !_disposed && !token.IsCancellationRequested && generation == _generation;

    private void ClearSelectedRepository()
    {
        _selectedDetails = null;
        _selectedFile = null;
        _files.ItemsSource = Array.Empty<HuggingFaceModelFile>();
        SetText(_repositoryDetails, "Select a repository to inspect its exact commit, license, gating and verified GGUF files.");
        UpdateInstallAvailability();
    }

    private void ClearProfileState(string status)
    {
        _boundActor = null;
        _repositories.ItemsSource = Array.Empty<HuggingFaceModelSummary>();
        ClearSelectedRepository();
        _hasToken = false;
        _installProgress.Value = 0;
        _installProgress.IsVisible = false;
        SetText(_installStatus, "No installation is running.");
        ClearTokenInput();
        SetText(_tokenStatus, "Profile-bound token status is unavailable.");
        SetText(_browserStatus, status);
        UpdateInstallAvailability();
    }

    private void ClearTokenInput(string? status = null)
    {
        _token.Text = string.Empty;
        _token.RevealPassword = false;
        if (status is not null) SetText(_tokenStatus, status);
    }

    private void UpdateInstallAvailability()
    {
        SetEnabled(_installButton, !_busy && _service is not null && _profile is not null && _boundActor is not null &&
            _selectedDetails is not null && _selectedFile is not null && (!_selectedDetails.Gated || _hasToken));
        SetEnabled(_removeTokenButton, _hasToken && !_busy);
        SetEnabled(_saveTokenButton, !_busy && _service is not null && _profile is not null);
        SetEnabled(_searchButton, !_busy && _service is not null && _profile is not null);
    }

    private static string SafeMessage(Exception error, string fallback) => error is HuggingFaceModelException typed
        ? typed.Code switch
        {
            HuggingFaceModelError.AuthenticationRequired => "Hugging Face access could not be verified. Check the saved account access, then retry.",
            HuggingFaceModelError.AccessDenied => "Hugging Face access could not be verified for this repository. Check account access, then retry.",
            HuggingFaceModelError.InvalidRequest => "The repository request is invalid; search again and choose a current result.",
            HuggingFaceModelError.Unavailable => "Hugging Face is temporarily unavailable. Retry the operation.",
            HuggingFaceModelError.InvalidMetadata => "The repository did not provide valid model-file metadata.",
            HuggingFaceModelError.DownloadRejected => "Hugging Face rejected this file download.",
            HuggingFaceModelError.IntegrityFailure => "The downloaded file failed integrity verification and was not installed.",
            HuggingFaceModelError.ImportFailed => "The verified model could not be imported into Dulche.",
            HuggingFaceModelError.SecureStorageUnavailable => "Secure token storage is unavailable. The token was not saved.",
            _ => fallback
        }
        : fallback;

    private static string FormatBytes(long bytes) => bytes < 0 ? "Unknown size"
        : bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GiB"
        : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.##} MiB" : $"{bytes:N0} bytes";

    private static string ShortCommit(string value) => value.Length <= 12 ? value : value[..12];
    private static void SetText(Control control, string value) =>
        CuiPropertyPresentation.Set(control, "Text", value, CuiPropertyLevel.Instance, "Home Hugging Face state", order: 1);
    private static void SetEnabled(Control control, bool value) =>
        CuiPropertyPresentation.Set(control, "IsEnabled", value, CuiPropertyLevel.Instance, "Home Hugging Face state", order: 1);

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var current = source;
        source = null;
        if (current is null) return;
        try { current.Cancel(); } catch (ObjectDisposedException) { }
        current.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        CancelAndDispose(ref _operationCancellation);
        CancelAndDispose(ref _installCancellation);
        ClearTokenInput();
        _loader.Dispose();
        Content = null;
    }
}

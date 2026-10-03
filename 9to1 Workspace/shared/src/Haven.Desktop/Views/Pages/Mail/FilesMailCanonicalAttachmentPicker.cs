using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Mail.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Mail;

/// <summary>Manual native selection of a privately issued canonical Files row. This grants no Mail send authority.</summary>
public sealed class FilesMailCanonicalAttachmentPicker(FilesNativeBrowserService browser,
    FilesArtifactResourceResolver originalOwner, IAuthenticatedResourceActorSource actors)
{
    internal Session? ActiveSession { get; private set; }

    internal static FilesMailCanonicalAttachmentPicker? FromServices(IServiceProvider services)
    {
        var browser = services.GetService<FilesNativeBrowserService>();
        var owner = services.GetService<FilesArtifactResourceResolver>();
        var actors = services.GetService<IAuthenticatedResourceActorSource>();
        return browser is null || owner is null || actors is null ? null : new(browser, owner, actors);
    }

    internal async Task<MailAttachmentContent?> PickAsync(Window? parent, Guid accountId, Guid draftId,
        Func<bool> originalComposeCurrent, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ActiveSession is not null) throw new InvalidOperationException("Finish the current attachment selection first.");
        if (!originalComposeCurrent()) throw new UnauthorizedAccessException("The original Mail draft retired.");
        var actor = await actors.GetCurrentAsync(token);
        if (actor is null || !originalComposeCurrent()) throw new UnauthorizedAccessException("The original Mail account or profile retired.");
        using var session = new Session(browser, originalOwner, actor, accountId, draftId, originalComposeCurrent, token);
        ActiveSession = session;
        try
        {
            await session.InitializeAsync(token);
            if (!originalComposeCurrent()) throw new UnauthorizedAccessException("The original Mail draft retired.");
            if (parent is null) session.Window.Show(); else session.Window.Show(parent);
            return await session.Result;
        }
        finally { ActiveSession = null; await session.CloseAndDrainAsync(); }
    }

    internal sealed class Session : ICuiWritableBindingContext, ICuiActionDispatcher,
        ICuiActionAvailability, INotifyPropertyChanged, IDisposable
    {
        private readonly FilesNativeBrowserService _browser;
        private readonly FilesArtifactResourceResolver _owner;
        private readonly AuthenticatedResourceActor _actor;
        private readonly Guid _account, _draft;
        private readonly Func<bool> _originalCurrent;
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationTokenRegistration _cancel;
        private readonly CuiSceneHost _host = new(new CuiControlRegistry());
        private readonly TaskCompletionSource<MailAttachmentContent?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private FilesNativeBrowserPage? _page;
        private int _index = -1;
        private object _selection = new();
        private bool _disposed, _busy;
        private string _query = "", _status = "Choose a canonical Files attachment.";
        private Task _pending = Task.CompletedTask;
        internal Window Window { get; } = new() { Title = "Attach from Files", Width = 640, Height = 420 };
        internal Task<MailAttachmentContent?> Result => _result.Task;
        internal Task PendingOperation => _pending;
        internal Session(FilesNativeBrowserService browser, FilesArtifactResourceResolver owner,
            AuthenticatedResourceActor actor, Guid account, Guid draft, Func<bool> current, CancellationToken token)
        {
            _browser = browser; _owner = owner; _actor = actor; _account = account; _draft = draft; _originalCurrent = current;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _cancel = _lifetime.Token.Register(() => Dispatcher.UIThread.Post(() => { if (!_disposed) Window.Close(); }));
            Window.Closed += (_, _) => Retire();
        }
        private bool Current => !Volatile.Read(ref _disposed) && !_lifetime.IsCancellationRequested && _originalCurrent();
        private HostedItemMetadata? Selected => _page is { } page && _index >= 0 && _index < page.Items.Count ? page.Items[_index] : null;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string path) => PropertyChanged?.Invoke(this, new(path));
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Rows" => _page?.Items.Select(x => $"{x.Kind} — {x.Name}").ToArray() ?? [],
                "SelectedIndex" => _index, "Search" => _query, "Status" => _status,
                "CanAttach" => IsActionAvailable("Attach"), "CanEnter" => IsActionAvailable("Enter"),
                "CanUp" => IsActionAvailable("Up"), "CanSearch" => IsActionAvailable("Search"), "CanSelect" => Current && !_busy, _ => null
            };
            return path is "Rows" or "SelectedIndex" or "Search" or "Status" or "CanAttach" or "CanEnter" or "CanUp" or "CanSearch" or "CanSelect";
        }
        public bool TrySetValue(string path, object? value)
        {
            if (!Current) return false;
            // A real selection change retires an in-flight original even while actions are busy.
            if (path == "Search" && !_busy && value is string text && text.Length <= 256) { _query = text; return true; }
            if (path != "SelectedIndex" || value is not int index || index < -1 || index >= (_page?.Items.Count ?? 0)) return false;
            _index = index; Interlocked.Exchange(ref _selection, new object());
            Changed("CanAttach"); Changed("CanEnter"); return true;
        }
        public bool HasAction(string command) => command is "Attach" or "Enter" or "Up" or "Search" or "Cancel";
        public bool? IsActionAvailable(string command) => HasAction(command) && Current && !_busy && (command switch
        {
            "Attach" => Selected?.Kind == HostedItemKind.File,
            "Enter" => Selected?.Kind == HostedItemKind.Folder,
            "Up" => _page?.ParentID is not null,
            "Search" => _page is not null,
            "Cancel" => true, _ => false
        });
        internal async Task InitializeAsync(CancellationToken token)
        {
            var original = await _browser.ListAsync(_actor, token: token);
            if (!Current) throw new UnauthorizedAccessException("The original Mail draft retired.");
            await _browser.RevalidateAsync(original, _actor, token);
            if (!Current) throw new UnauthorizedAccessException("The original Files selection retired.");
            _page = original;
            const string resource = "Haven.Desktop.Resources.Cui.MailFilesAttachmentPicker.cui";
            using var stream = typeof(FilesMailCanonicalAttachmentPicker).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidDataException("The native Files attachment picker scene is missing.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser(); var document = parser.Parse(await reader.ReadToEndAsync(token), resource);
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error)) throw new InvalidDataException("The attachment picker scene is invalid.");
            var ready = await _host.ShowAsync(new("mail.attach.files", "Attach from Files", "Mail", document, this, this, new Readiness(this)), token);
            if (ready.State != CuiSceneAvailabilityState.Ready || !Current) throw new UnauthorizedAccessException("The original attachment picker is unavailable.");
            Window.Content = _host;
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("This attachment action is unavailable.");
            if (command == "Cancel") { Window.Close(); return ValueTask.CompletedTask; }
            _pending = RunAsync(command, cancellationToken); return new(_pending);
        }
        private async Task RunAsync(string command, CancellationToken token)
        {
            var page = _page!; var row = Selected; var query = _query; var selection = Volatile.Read(ref _selection);
            bool Original() => Current && ReferenceEquals(_page, page) && ReferenceEquals(Volatile.Read(ref _selection), selection);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            _busy = true; AvailabilityChanged();
            try
            {
                if (command == "Attach")
                {
                    var content = await _browser.ReadOriginalMailAttachmentSelectionAsync(page, row!, _actor,
                        _owner, _account, _draft, Original, linked.Token);
                    if (!Original()) throw new UnauthorizedAccessException("The original attachment selection retired.");
                    _result.TrySetResult(content); Window.Close(); return;
                }
                Guid? folder = command == "Enter" ? row!.Id.Value : page.ParentID;
                if (command == "Up")
                {
                    var parent = await _browser.GetParentAsync(page, _actor, linked.Token);
                    if (!Original()) throw new UnauthorizedAccessException("The original Files folder retired.");
                    folder = parent.ID;
                }
                var next = await _browser.ListAsync(_actor, folder, command == "Search" ? query : "", token: linked.Token, expectedStoreId: page.StoreID);
                if (!Original()) throw new UnauthorizedAccessException("The original attachment picker retired.");
                await _browser.RevalidateAsync(next, _actor, linked.Token);
                if (!Original()) throw new UnauthorizedAccessException("The original Files page retired.");
                _page = next; _index = -1; _query = command == "Search" ? query : ""; Interlocked.Exchange(ref _selection, new object());
                foreach (var path in new[] { "Rows", "SelectedIndex", "Search" }) Changed(path);
                _status = next.Next is null ? $"{next.Items.Count} current folder items." : $"Showing the first {next.Items.Count} items. Narrow the search to find another file.";
                Changed("Status");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !Current) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            { if (Original()) { _status = "This original Files selection is unavailable. Refresh or choose another file."; Changed("Status"); } }
            finally { _busy = false; if (Current) AvailabilityChanged(); }
        }
        private void AvailabilityChanged() { foreach (var path in new[] { "CanAttach", "CanEnter", "CanUp", "CanSearch", "CanSelect" }) Changed(path); }
        private void Retire()
        { if (_disposed) return; _disposed = true; _lifetime.Cancel(); Interlocked.Exchange(ref _selection, new object()); _result.TrySetResult(null); }
        internal async Task CloseAndDrainAsync()
        { Window.Close(); Retire(); try { await _pending; } finally { Window.Content = null; _host.Dispose(); } }
        public void Dispose() { Retire(); _cancel.Dispose(); _lifetime.Dispose(); }
        private sealed class Readiness(Session owner) : ICuiSceneReadiness
        {
            public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
            { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(owner.Current
                ? new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "OriginalMailCompose", "Original Mail draft retained.")
                : new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "MailComposeRetired", "Reopen the Mail draft.")); }
        }
    }
}

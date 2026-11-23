using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Only an explicit false current-owner predicate produces this expected denial.
/// Actual Home, storage and caller faults keep their original cause and Task status.</summary>
internal sealed class HomeSettingsAuthorityRefusal(string message) : Exception(message);

/// <summary>The same canonical Home Appearance fragment and native preference owner used by
/// standalone Home. This tab borrows the source/context; it never disposes either or owns a file.</summary>
internal sealed class HomeSettingsDestinationPage : UserControl, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly NativeHomeThemePreferences _source;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly CuiControlLoader _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
    private readonly CuiViewModel _bindings = new();
    private readonly DesktopOriginalWorkLifetime _work;
    private sealed record CategoryRow(string Key, string? Parent, Button Button);
    private readonly CategoryRow[] _categoryRows;
    private readonly TextBox _search = new();
    private string _category = "theme";
    private string _query = string.Empty;
    private Avalonia.Vector _offsetBeforeSearch;
    private bool _globalExpanded = true;
    private bool _havenExpanded;
    private Task? _initialization;
    private Task<bool>? _preparation;
    private bool _active, _unavailable, _stopped;
    private int _busyCount;
    private bool IsBusy => _busyCount != 0;
    private long _generation;
    private string? _status;
    internal sealed record AdmissionObservation(Task? Source, Exception Cause, CancellationToken RequestedToken);
    private readonly List<AdmissionObservation> _admissionObservations = [];
    internal IReadOnlyList<AdmissionObservation> AdmissionObservations => _admissionObservations.ToArray();

    internal HomeSettingsDestinationPage(NativeHomeThemePreferences source, Func<CancellationToken, Task> admit)
    {
        Dispatcher.UIThread.VerifyAccess();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _work = new(StopAsync, CleanupAsync);
        _loader.SetThemePreferenceContext(source.Context);
        _loader.SetBindingContext(_bindings); _loader.SetActionDispatcher(this);
        _loader.BindAuthoredControl("home-settings-search", _ => _search);
        NativeHomeThemePreferences.BindAppearanceLayout(_loader, _bindings);
        _categoryRows = new (string Key, string? Parent)[]
        {
            ("global", null), ("appearance", "global"), ("theme", "appearance"),
            ("theme-options", "appearance"), ("layout", "appearance"), ("accessibility", "appearance"),
            ("haven", null), ("haven-general", "haven"), ("haven-bookmarks", "haven"), ("haven-history", "haven")
        }.Select(category =>
        {
            var button = new Button();
            _loader.BindAuthoredControl("home-settings-" + category.Key, _ => button);
            return new CategoryRow(category.Key, category.Parent, button);
        }).ToArray();
        SetBrowseStatus(string.Empty);
        PublishCategory();
        ApplyResponsiveLayout(double.PositiveInfinity);
        var loaded = _loader.TryLoad(LoadSettingsDocument());
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Home Settings could not be rendered: " + string.Join("; ",
                loaded.Diagnostics.Select(item => item.Code + " " + item.Message)));
        _loader.WireBindings(loaded.Root); Content = loaded.Root;
        PublishCategory();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AddHandler(KeyDownEvent, OnCategoryKeyDown, RoutingStrategies.Tunnel);
        _search.TextChanged += (_, _) => FilterCategories();
        _bindings.PropertyChanged += NativeHomeThemePreferences.UpdateDisplayReadout;
        _source.Changed += OnSourceChanged;
        RefreshBindings();
    }

    internal bool IsAlive => !_work.IsRetiring;
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    internal NativeHomeThemePreferences.SettingsWriteOutcome? LastWrite { get; private set; }
    internal Func<bool, CancellationToken, Task>? NavigateHomeAsync { get; set; }
    internal Func<BrowserNativePage, CancellationToken, Task<bool>>? NavigateBrowseAsync { get; set; }
    internal string CategoryTitle => _category switch
    {
        "theme" => "Theme and accent preferences",
        "theme-options" => "Theme Options",
        "layout" => "Layout",
        "accessibility" => "Accessibility",
        "haven-general" => "General",
        "haven-bookmarks" => "Bookmarks",
        "haven-history" => "History",
        _ => "Appearance"
    };
    internal event EventHandler? CategoryChanged;

    internal Task ActivateAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_active)
        {
            _category = "theme";
            _query = string.Empty;
            _search.Text = string.Empty;
            _globalExpanded = true;
            _havenExpanded = false;
            SetBrowseStatus(string.Empty);
            PublishCategory();
        }
        _active = true;
        return _work.RunAsync(original => RefreshAsync(original, token), actual => _initialization ??= actual);
    }
    internal void Deactivate()
    { Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation; }

    internal void FocusCategory()
    {
        _categoryRows.Single(row => row.Key == "theme").Button.Focus(NavigationMethod.Tab);
        SelectCategory("theme");
    }

    public bool? IsActionAvailable(string command) => IsAlive && _active && (command switch
    {
        "SettingsBack" or "SettingsOpenDashboard" => NavigateHomeAsync is not null,
        "SettingsGlobal" or "SettingsAppearance" or "SettingsTheme" or "SettingsThemeOptions" or "SettingsLayout" or "SettingsAccessibility" => true,
        "SettingsOptionsAccent" or "SettingsOptionsAccessibility" => _category == "theme-options",
        "SettingsHaven" or "SettingsHavenGeneral" or "SettingsHavenBookmarks" or "SettingsHavenHistory" => NavigateBrowseAsync is not null,
        "SettingsOpenBrowseGeneral" => NavigateBrowseAsync is not null && _category == "haven-general",
        "SettingsOpenBrowseBookmarks" => NavigateBrowseAsync is not null && _category == "haven-bookmarks",
        "SettingsOpenBrowseHistory" => NavigateBrowseAsync is not null && _category == "haven-history",
        "ApplyHomeAppearance" or "RefreshHomeAppearance" => !IsBusy && !_unavailable,
        _ => false
    });

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_work.IsRetiring) return ValueTask.CompletedTask; // Already queued presentation acquires no new business work.
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Home Settings action is unavailable.");
        switch (command)
        {
            case "SettingsBack":
            case "SettingsOpenDashboard":
                return new(NavigateHomeAsync!(command == "SettingsOpenDashboard", cancellationToken));
            case "SettingsGlobal":
            case "SettingsHaven":
                if (_query.Length == 0)
                {
                    var group = command == "SettingsGlobal" ? "global" : "haven";
                    SetGroupExpanded(group, !IsGroupExpanded(group));
                }
                return ValueTask.CompletedTask;
            case "SettingsAppearance":
            case "SettingsTheme":
            case "SettingsLayout":
            case "SettingsAccessibility":
                SelectCategory(command["Settings".Length..].ToLowerInvariant());
                return ValueTask.CompletedTask;
            case "SettingsThemeOptions":
                SelectCategory("theme-options");
                return ValueTask.CompletedTask;
            case "SettingsOptionsAccent":
                OpenThemeOptionsLink("theme", "home-accent-settings");
                return ValueTask.CompletedTask;
            case "SettingsOptionsAccessibility":
                OpenThemeOptionsLink("accessibility");
                return ValueTask.CompletedTask;
            case "SettingsHavenGeneral":
            case "SettingsHavenBookmarks":
            case "SettingsHavenHistory":
                SelectCategory("haven-" + command["SettingsHaven".Length..].ToLowerInvariant());
                return ValueTask.CompletedTask;
            case "SettingsOpenBrowseGeneral":
                return OpenBrowsePageAsync(BrowserNativePage.Settings, cancellationToken);
            case "SettingsOpenBrowseBookmarks":
                return OpenBrowsePageAsync(BrowserNativePage.Bookmarks, cancellationToken);
            case "SettingsOpenBrowseHistory":
                return OpenBrowsePageAsync(BrowserNativePage.History, cancellationToken);
        }
        parameter = NativeHomeThemePreferences.CaptureDisplayDraft(parameter, _bindings);
        return new(_work.RunAsync(async original =>
        {
            if (command == "RefreshHomeAppearance")
            {
                NativeHomeThemePreferences.ReloadDisplayDraft(_bindings);
                await RefreshAsync(original, cancellationToken);
                return;
            }
            var generation = _generation;
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
            cancellationToken.ThrowIfCancellationRequested();
            NativeHomeThemePreferences.SettingsWriteOutcome? write = null;
            _busyCount++;
            try
            {
                RefreshBindings();

                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, false); return; }
                if (!original.IsAcceptedPublicationCurrent) return;
                cancellationToken.ThrowIfCancellationRequested();
                write = LastWrite = _source.CreateSettingsWriteOutcome();
                // The preference owner retains its actual business lifetime. A tab's private
                // retirement token is not permission to cancel a global atomic preference write.
                var receipt = await original.AwaitAsync(write.ApplyAsync(parameter));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                { Deny(original, receipt.Succeeded); return; }
                original.RunAcceptedPublicationCallback(() =>
                {
                    _status = receipt.Succeeded ? "Appearance saved." : receipt.Diagnostic ??
                        "Appearance changed. Refresh before choosing again.";
                    RefreshBindings();
                });
            }
            catch (Exception cause)
            {
                if (write?.RetainPostCommitFailure(null, cause) == true)
                {
                    // AwaitAsync already retains an actual failed source. Preserve the confirmed
                    // save outcome independently; no UI observer can replay Set or invent failure.
                    original.Retain(cause);
                    Deny(original, true);
                    return;
                }
                if (original.IsAcceptedPublicationCurrent) Deny(original, false);
                throw;
            }
            finally
            {
                _busyCount--;
                if (original.IsAcceptedPublicationCurrent)
                    try { original.RunAcceptedPublicationCallback(RefreshBindings); }
                    catch (Exception cause)
                    {
                        if (write?.RetainPostCommitFailure(null, cause) != true) throw;
                        original.Retain(cause); _unavailable = true;
                        _status = NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic;
                    }
            }
        }));
    }

    private ValueTask OpenBrowsePageAsync(BrowserNativePage page, CancellationToken token)
    {
        var navigate = NavigateBrowseAsync!;
        var generation = _generation;
        var category = _category;
        return new(_work.RunAsync(async original =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
            original.BindPublicationGuard(() => _active && IsAlive && generation == _generation && category == _category);
            linked.Token.ThrowIfCancellationRequested();
            var admitted = await original.AwaitAsync(IsAdmittedAsync(linked.Token));
            if (!original.IsAcceptedPublicationCurrent) return;
            if (!admitted)
            {
                original.RunAcceptedPublicationCallback(() => SetBrowseStatus("Reopen Settings for the current profile."));
                return;
            }
            linked.Token.ThrowIfCancellationRequested();
            var accepted = await original.AwaitAsync(navigate(page, linked.Token));
            original.RunAcceptedPublicationCallback(() => SetBrowseStatus(accepted ? string.Empty
                : "Browse could not open this page. Try again."));
        }));
    }

    private void SetBrowseStatus(string status)
    {
        _bindings.Set("SettingsBrowseStatus", status);
        _bindings.Set("SettingsHasBrowseStatus", status.Length != 0);
    }

    private static CuiDocument LoadSettingsDocument()
    {
        using var stream = typeof(HomeSettingsDestinationPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Settings.cui")
            ?? throw new InvalidDataException("The Home Settings document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "HomeSettings.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home Settings document is invalid: " + string.Join("; ",
                parser.Diagnostics.Diagnostics.Select(item => item.Code + " " + item.Message)));
        var home = HavenOS.Home.HomeCuiSurface.LoadDefault().Document;
        var appearance = home.Components
            .SelectMany(component => component.DescendantsAndSelf())
            .Single(component => component.AuthoredId == "home-appearance-settings");
        return document with
        {
            Actions = home.Actions,
            Components = document.Components.Select(IncludeAppearance).ToArray()
        };

        CuiComponent IncludeAppearance(CuiComponent component) => new(component.Type, component.Name,
            component.Classes, component.Properties, component.Actions,
            component.AuthoredId == "home-settings-appearance-host" ? [appearance] : component.Children.Select(IncludeAppearance).ToArray(),
            component.Condition, component.List, component.Span, component.DefaultTheme, component.Repeat,
            component.ElseChildren, component.Groups, component.IsDefinition, component.PropertyRegion)
        { Text = component.Text, TextParts = component.TextParts };
    }

    private void ApplyResponsiveLayout(double width)
    {
        var narrow = width < 790;
        _bindings.Set("SettingsColumns", narrow ? "1fr" : width >= 1120 ? "276 1fr" : "232 1fr");
        _bindings.Set("SettingsRows", narrow ? "Auto 1fr" : "1fr");
        _bindings.Set("SettingsContentRow", narrow ? 1 : 0);
        _bindings.Set("SettingsContentColumn", narrow ? 0 : 1);
        _bindings.Set("SettingsNavHeight", narrow ? 176d : double.PositiveInfinity);
        _bindings.Set("SettingsOptionsPadding", narrow ? "14 20 14 24" : "48 25 36 32");
        _bindings.Set("SettingsOptionsRows", narrow ? "Auto Auto" : "Auto");
        _bindings.Set("SettingsOptionsColumns", narrow ? "1fr" : "1fr Auto");
        _bindings.Set("SettingsOptionsControlRow", narrow ? 1 : 0);
        _bindings.Set("SettingsOptionsControlColumn", narrow ? 0 : 1);
    }

    private bool Matches(string terms) => terms.Contains(_query, StringComparison.OrdinalIgnoreCase);

    private ScrollViewer? CategoryScroll() => this.GetVisualDescendants().OfType<CuiContainerControl>()
        .FirstOrDefault(control => control.Name == "home-settings-categories")?.Child as ScrollViewer;

    private void FilterCategories()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        if (_query == query) return;
        if (_query.Length == 0) _offsetBeforeSearch = CategoryScroll()?.Offset ?? default;
        _query = query;
        PublishCategory();
        Dispatcher.UIThread.Post(() =>
        {
            if (query == _query && CategoryScroll() is { } scroll)
                scroll.Offset = query.Length == 0 ? _offsetBeforeSearch : default;
        }, DispatcherPriority.Loaded);
    }

    private void PublishCategory()
    {
        var parent = Matches("Global Settings") || Matches("Appearance");
        var theme = parent || Matches("Theme and accent preferences") || Matches("Brightness") ||
            Matches("Colour contrast") || Matches("Override app accents");
        var themeOptions = parent || Matches("Theme Options") || Matches("Light Dark Follow system") || Matches("Accent palette");
        var layout = parent || Matches("Layout") || Matches("Dashboard") || Matches("Recover saved appearance");
        var accessibility = parent || Matches("Accessibility") || Matches("High contrast") || Matches("Reduce motion");
        var any = theme || themeOptions || layout || accessibility;
        var expanded = (_query.Length > 0 || _globalExpanded) && any;
        var hasBrowse = NavigateBrowseAsync is not null;
        var havenGroup = Matches("9to1 Haven Tabs and browsing");
        var general = hasBrowse && (havenGroup || Matches("General Browse settings engine privacy startup restore tabs"));
        var bookmarks = hasBrowse && (havenGroup || Matches("Bookmarks saved destinations"));
        var history = hasBrowse && (havenGroup || Matches("History previous web visits"));
        var anyHaven = general || bookmarks || history;
        var havenExpanded = (_query.Length > 0 || _havenExpanded) && anyHaven;
        _bindings.Set("SettingsGlobalExpanded", expanded);
        _bindings.Set("SettingsGlobalChevron", expanded ? "⌄" : "›");
        _bindings.Set("SettingsMatchGlobal", any);
        _bindings.Set("SettingsMatchChildren", any);
        _bindings.Set("SettingsMatchTheme", theme);
        _bindings.Set("SettingsMatchThemeOptions", themeOptions);
        _bindings.Set("SettingsMatchLayout", layout);
        _bindings.Set("SettingsMatchAccessibility", accessibility);
        _bindings.Set("SettingsMatchHaven", anyHaven);
        _bindings.Set("SettingsHavenExpanded", havenExpanded);
        _bindings.Set("SettingsHavenChevron", havenExpanded ? "⌄" : "›");
        _bindings.Set("SettingsMatchHavenGeneral", general);
        _bindings.Set("SettingsMatchHavenBookmarks", bookmarks);
        _bindings.Set("SettingsMatchHavenHistory", history);
        _bindings.Set("SettingsNoMatches", !any && !anyHaven);
        _bindings.Set("SettingsShowsAppearance", _category is "theme" or "accessibility");
        _bindings.Set("SettingsLayoutSelected", _category == "layout");
        _bindings.Set("SettingsAppearanceSelected", _category == "appearance");
        _bindings.Set("SettingsThemeSelected", _category == "theme");
        _bindings.Set("SettingsThemeOptionsSelected", _category == "theme-options");
        _bindings.Set("SettingsAccessibilitySelected", _category == "accessibility");
        _bindings.Set("SettingsHavenSelected", _category.StartsWith("haven-", StringComparison.Ordinal));
        _bindings.Set("SettingsHavenGeneralSelected", _category == "haven-general");
        _bindings.Set("SettingsHavenBookmarksSelected", _category == "haven-bookmarks");
        _bindings.Set("SettingsHavenHistorySelected", _category == "haven-history");
        _bindings.Set("SettingsHavenTitle", CategoryTitle);
        _bindings.Set("SettingsHavenDescription", _category switch
        {
            "haven-general" => "Manage your default browser engine, history recording and whether Haven restores tabs at startup.",
            "haven-bookmarks" => "Open or remove bookmarks saved in Browse.",
            "haven-history" => "Search previous web visits and manage local browsing history.",
            _ => string.Empty
        });
        foreach (var row in _categoryRows)
            AutomationProperties.SetItemStatus(row.Button, row.Key switch
            {
                "global" => expanded ? "Expanded" : "Collapsed",
                "haven" => havenExpanded ? "Expanded" : "Collapsed",
                _ => row.Key == _category ? "Current page" : null
            });
    }

    private void OpenThemeOptionsLink(string category, string? anchor = null)
    {
        _search.Text = string.Empty;
        SelectCategory(category, anchor);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || !IsAlive || _category != category) return;
            var row = _categoryRows.Single(row => row.Key == category).Button;
            row.Focus(NavigationMethod.Tab);
            row.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void SelectCategory(string category, string? requestedAnchor = null)
    {
        var changed = _category != category;
        _category = category;
        if (category.StartsWith("haven-", StringComparison.Ordinal)) _havenExpanded = true;
        else _globalExpanded = true;
        if (changed) SetBrowseStatus(string.Empty);
        PublishCategory();
        if (changed) CategoryChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active || !IsAlive || _category != category || category is not ("theme" or "accessibility")) return;
            var appearance = this.GetVisualDescendants().OfType<CuiContainerControl>()
                .Single(control => control.Name == "home-appearance-settings");
            var anchor = requestedAnchor ?? (category == "accessibility" ? "home-appearance-accessibility"
                : _query.Length == 0 ? null
                : Matches("Accent palette") || Matches("Override app accents") ? "home-accent-settings"
                : Matches("Brightness") || Matches("Colour contrast") ? "home-display-settings" : null);
            if (anchor is null)
                appearance.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHome();
            else
                appearance.GetVisualDescendants().OfType<Control>().First(control => control.Name == anchor).BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void OnCategoryKeyDown(object? sender, KeyEventArgs args)
    {
        if (!_active || args.KeyModifiers != KeyModifiers.None) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var visible = _categoryRows.Where(row => row.Button.IsEffectivelyVisible && row.Button.IsAttachedToVisualTree()).ToArray();
        var index = Array.FindIndex(visible, row => ReferenceEquals(row.Button, focused));
        if (_query.Length > 0 && args.Key == Key.Escape && (focused == _search || index >= 0))
        {
            _search.Text = string.Empty;
            _search.Focus(NavigationMethod.Tab);
            args.Handled = true;
            return;
        }
        if (focused == _search && args.Key is Key.Down or Key.Enter)
        {
            var result = (visible.FirstOrDefault(row => row.Parent is not null &&
                    _query.Equals(AutomationProperties.GetName(row.Button), StringComparison.OrdinalIgnoreCase))
                ?? visible.FirstOrDefault(row => row.Parent is not null && row.Key != "appearance"))?.Button;
            if (result is null) return;
            args.Handled = true;
            result.Focus(NavigationMethod.Directional);
            result.BringIntoView();
            if (args.Key == Key.Enter) result.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        if (index < 0) return;
        var current = visible[index];
        if (current.Parent is null && _query.Length == 0 &&
            (args.Key == Key.Left && IsGroupExpanded(current.Key) || args.Key == Key.Right && !IsGroupExpanded(current.Key)))
        {
            SetGroupExpanded(current.Key, args.Key == Key.Right);
            args.Handled = true;
            return;
        }
        var next = args.Key switch
        {
            Key.Up => visible[Math.Max(0, index - 1)],
            Key.Down => visible[Math.Min(visible.Length - 1, index + 1)],
            Key.Home => visible[0],
            Key.End => visible[^1],
            Key.Left => visible.FirstOrDefault(row => row.Key == current.Parent),
            Key.Right => visible.FirstOrDefault(row => row.Parent == current.Key),
            _ => null
        };
        if (next is null) return;
        args.Handled = true;
        next.Button.Focus(NavigationMethod.Directional);
        next.Button.BringIntoView();
    }

    private bool IsGroupExpanded(string group) => group == "global" ? _globalExpanded : _havenExpanded;

    private void SetGroupExpanded(string group, bool expanded)
    {
        if (group == "global") _globalExpanded = expanded;
        else _havenExpanded = expanded;
        PublishCategory();
    }

    private async Task<bool> IsAdmittedAsync(CancellationToken token)
    {
        // Only the owning producer's explicit denial is a normal unavailable result. Real
        // source/caller cancellation and IO exceptions keep the SAME original failed task.
        Task actual;
        try { actual = _admit(token) ?? throw new InvalidOperationException("The original Home Settings admission returned no Task."); }
        catch (OperationCanceledException cause)
        {
            _admissionObservations.Add(new(null, cause, token));
            throw new AggregateException("The Home Settings authority callback faulted before returning its source.", cause);
        }
        try { await actual; return true; }
        catch (Exception cause)
        {
            // Capture the raw terminal payload once: Task.Exception need not return the same
            // wrapper on repeated reads. Only a sole actual owner refusal is a quiet denial.
            var terminal = actual.IsFaulted ? actual.Exception : null;
            var failures = terminal?.InnerExceptions;
            if (cause is HomeSettingsAuthorityRefusal && failures is { Count: 1 } &&
                failures[0] is HomeSettingsAuthorityRefusal) return false;
            // Await exposes only one cause. A refusal sharing its raw Task with an I/O or
            // cancellation failure must preserve the entire terminal payload.
            var observed = failures is { Count: > 1 } ? terminal! : cause;
            _admissionObservations.Add(new(actual, observed, token));
            if (actual.IsFaulted && (cause is OperationCanceledException || failures is { Count: > 1 }))
                throw new AggregateException("The original Home Settings authority source faulted.", failures!);
            throw;
        }
    }

    private async Task RefreshAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
    {
        var generation = ++_generation;
        original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
        caller.ThrowIfCancellationRequested();
        _busyCount++;
        try
        {
            RefreshBindings();

            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            if (!original.IsAcceptedPublicationCurrent) return;
            // Borrow, do not repoint or dispose, the SAME live source/context used by Home chrome.
            await original.AwaitAsync(_source.RefreshAsync());
            if (!original.IsAcceptedPublicationCurrent) return;
            caller.ThrowIfCancellationRequested();
            if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
            original.RunAcceptedPublicationCallback(() => { _unavailable = false; _status = _source.Diagnostic; });
        }
        catch
        {
            if (original.IsAcceptedPublicationCurrent) Deny(original, false);
            throw;
        }
        finally
        {
            _busyCount--;
            if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
        }
    }

    private void Deny(DesktopOriginalWorkLifetime.Original original, bool saved)
    {
        if (!original.IsAcceptedPublicationCurrent) return;
        try
        {
            original.RunAcceptedPublicationCallback(() =>
            {
                _unavailable = true;
                _status = saved ? NativeHomeThemePreferences.SettingsWriteOutcome.SavedObservationDiagnostic
                    : "Appearance is unavailable. Reopen Home for the current profile.";
                RefreshBindings();
            });
        }
        catch (Exception cause) when (saved)
        {
            LastWrite?.RetainPostCommitFailure(null, cause);
            original.Retain(cause); // The confirmed save remains distinguishable from its broken observer.
        }
    }
    private void RefreshBindings()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_active && !_unavailable)
            _source.ApplyEditorBindings(_bindings);
        else
        {
            _bindings.Set("HomeAppearanceScope", "Applies to this device profile.");
            _bindings.Set("HomeThemeChoices", Array.Empty<object>());
            _bindings.Set("HomeAppearanceChoices", Array.Empty<object>());
            _bindings.Set("HomeModeChoices", Array.Empty<object>());
            _bindings.Set("HomeDisplayTarget", null);
            _bindings.Set("HomeAccessibilityChoices", Array.Empty<object>());
            _bindings.Set("HomeAccentModeChoices", Array.Empty<object>());
            _bindings.Set("HomeGlobalAccentChoices", Array.Empty<object>());
            _bindings.Set("HomeSavedGlobalColour", null);
        }
        var available = _active && IsAlive && !IsBusy && !_unavailable;
        _bindings.Set("HomeAppearanceAvailable", available);
        _bindings.Set("HomeAppearanceCanRefresh", available);
        _bindings.Set("SettingsHasAppearanceStatus", !string.IsNullOrWhiteSpace(_status));
        if (_status is not null) _bindings.Set("HomeAppearanceStatus", _status);
        _loader.RefreshBindings();
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        if (_work.IsRetiring) return;
        Task actual;
        try
        {
            actual = _work.RunAsync(async original =>
            {
                var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
                {
                    if (!_active || IsBusy || _unavailable) return;
                    var generation = _generation;
                    original.BindPublicationGuard(() => _active && IsAlive && generation == _generation);
                    if (!await original.AwaitAsync(IsAdmittedAsync(original.Token))) { Deny(original, false); return; }
                    if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(RefreshBindings);
                }).GetTask();
                var callback = await original.AwaitAsync(dispatch).ConfigureAwait(false);
                await original.AwaitAsync(callback).ConfigureAwait(false);
            });
        }
        catch (ObjectDisposedException) when (_work.IsRetiring) { return; }
        ObserveNotification(actual);
    }
    private static async void ObserveNotification(Task actual)
    {
        try { await actual; }
        catch (Exception cause) { System.Diagnostics.Trace.TraceError("Retained Home Appearance notification failed: {0}", cause); }
    }
    internal Task<bool> TryPrepareToCloseAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandExternalClose(); token.ThrowIfCancellationRequested();
        if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
        return _preparation = _work.RunAsync(_ => Task.FromResult(true));
    }
    internal Task CloseAndDrainAsync() => _work.CloseAndDrainAsync();
    private async Task StopAsync()
    {
        Dispatcher.UIThread.VerifyAccess(); _active = false; ++_generation;
        _source.Changed -= OnSourceChanged;
        // Seal page admission first. Let admitted CUI actions acknowledge their same global
        // writes before disposing this loader's caller token. The shared source remains live.
        var failures = new List<Exception>();
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception cause)
            {
                foreach (var error in actual.Exception?.InnerExceptions ?? new[] { cause }.AsEnumerable())
                    if (!failures.Any(known => ReferenceEquals(known, error))) failures.Add(error);
            }
        }
        // A failed action is still a terminal acknowledgement. Join it, retain its causes,
        // then join the loader's actual disposal/late-dispatch receipts as the production host does.
        await Join(_loader.WhenActionsIdleAsync());
        var disposed = false;
        try { _loader.Dispose(); disposed = true; } catch (Exception cause) { failures.Add(cause); }
        await Join(_loader.WhenActionsIdleAsync());
        _stopped = disposed;
        if (failures.Count != 0) throw new AggregateException("Original Home Settings actions or loader disposal failed.", failures);
    }
    private Task CleanupAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_stopped) throw new InvalidOperationException("The original Settings actions must settle before detachment.");
        NavigateHomeAsync = null;
        NavigateBrowseAsync = null;
        Content = null; return Task.CompletedTask;
    }
}

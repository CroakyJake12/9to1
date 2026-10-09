using Avalonia.Threading;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Pages.Write;
#if !ANDROID
using Haven.Desktop.Views.Pages.Assistants;
#endif

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    internal OriginalDocumentClosePreflight CaptureOriginalDocumentClosePreflight() =>
        new(CaptureOriginalDocumentCloseSubjects, () => !IsDisposed && !_originalShellWork.IsRetiring);

    private object?[] CaptureOriginalDocumentCloseSubjects()
    {
        Dispatcher.UIThread.VerifyAccess();
        var subjects = new List<object?> { this, SelectedTab, _secondaryTab, _currentPage, _currentChat };
        subjects.AddRange(CaptureCurrentOriginalShellChildren()); // Includes retained actual factory products.
        var tabs = OpenTabs.Concat(_secondaryTab is { } secondary ? [secondary] : [])
            .Distinct<WorkspaceTabViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var tab in tabs)
        {
            subjects.Add(tab);
            subjects.Add(tab.Page);
            subjects.AddRange(tab.CaptureOriginalPageCohort()); // SAME current/back/forward/abandoned objects.
        }
        return subjects.ToArray();
    }

    private OriginalDocumentClosePreflight CaptureOriginalDocumentTabClosePreflight(WorkspaceTabViewModel tab) =>
        new(() => CaptureOriginalDocumentTabCloseSubjects(tab),
            () => !IsDisposed && !_originalShellWork.IsRetiring && OpenTabs.Contains(tab));

    internal static object?[] CaptureOriginalDocumentTabCloseSubjects(WorkspaceTabViewModel tab) =>
        new object?[] { tab, tab.Page }.Concat(tab.CaptureOriginalPageCohort()).ToArray();
}

// A finite pre-close observation of actual native document references. This is
// not a drain receipt, permission, or a claim that unknown shell children retired.
internal sealed class OriginalDocumentClosePreflight
{
    private readonly Func<object?[]> _captureCurrent;
    private readonly Func<bool> _ownerCurrent;
    private readonly object?[] _subjects;
    private readonly object[] _documents;

    internal OriginalDocumentClosePreflight(Func<object?[]> captureCurrent, Func<bool> ownerCurrent)
    {
        Dispatcher.UIThread.VerifyAccess();
        _captureCurrent = captureCurrent ?? throw new ArgumentNullException(nameof(captureCurrent));
        _ownerCurrent = ownerCurrent ?? throw new ArgumentNullException(nameof(ownerCurrent));
        _subjects = _captureCurrent();
        _documents = _subjects.Where(IsPreflightDocument).OfType<object>()
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
    }

    internal bool HasDocuments => _documents.Length != 0;

    internal async Task<bool> PrepareAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        caller.ThrowIfCancellationRequested();
        if (!IsSameCurrentCohort) return false;
        foreach (var document in _documents)
        {
            caller.ThrowIfCancellationRequested();
            var close = OriginalClose(document);
            if (close is not null)
            {
                // Only this SAME terminal successful original is a finished owner.
                // Pending/faulted/canceled originals are never reopened or waived.
                if (!close.IsCompletedSuccessfully) return false;
                continue;
            }
            var prepared = document switch
            {
                WritePage write => await write.PrepareToCloseAsync("Save before closing Write", caller),
                PresentPage present => await present.PrepareToCloseAsync("Save before closing Present", caller),
#if !ANDROID
                NativeAssistantsDesktopPage assistants => await assistants.PrepareToCloseAsync(caller),
                NativeAssistantsSetupDesktopPage setup => await setup.PrepareToCloseAsync(caller),
#endif
                _ => throw new InvalidOperationException("The original document cohort changed type.")
            };
            if (!prepared || !IsSameCurrentCohort) return false;
        }
        return IsCurrentAndPrepared;
    }

    internal bool IsCurrentAndPrepared
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsSameCurrentCohort) return false;
            foreach (var document in _documents)
            {
                if (OriginalClose(document) is { } close)
                {
                    if (!close.IsCompletedSuccessfully) return false;
                }
                else if (document switch
                {
                    WritePage write => !write.IsOriginalDocumentClosePrepared,
                    PresentPage present => !present.IsOriginalDocumentClosePrepared,
#if !ANDROID
                    NativeAssistantsDesktopPage assistants => !assistants.IsOriginalClosePrepared,
                    NativeAssistantsSetupDesktopPage setup => !setup.IsOriginalClosePrepared,
#endif
                    _ => true
                }) return false;
            }
            return IsSameCurrentCohort; // Readiness callbacks cannot replace the retained source cohort.
        }
    }

    private bool IsSameCurrentCohort
    {
        get
        {
            if (!_ownerCurrent()) return false;
            var current = _captureCurrent();
            if (current.Length != _subjects.Length) return false;
            for (var index = 0; index < current.Length; index++)
                if (!ReferenceEquals(current[index], _subjects[index])) return false;
            return _ownerCurrent();
        }
    }

    private static bool IsPreflightDocument(object? document) => document is WritePage or PresentPage
#if !ANDROID
        || document is NativeAssistantsDesktopPage or NativeAssistantsSetupDesktopPage
#endif
        ;

    private static Task? OriginalClose(object document) => document switch
    {
        WritePage write => write.OriginalClose,
        PresentPage present => present.OriginalClose,
#if !ANDROID
        NativeAssistantsDesktopPage assistants => assistants.OriginalClose,
        NativeAssistantsSetupDesktopPage setup => setup.OriginalClose,
#endif
        _ => throw new InvalidOperationException("The original document cohort changed type.")
    };
}

using System.ComponentModel;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Study;

internal enum StudyWorkspaceRoute { Overview, Subjects, Progress, RevisionBank, Planner, Subject, StartSession, CreateSubject }
internal sealed record StudyWorkspaceIntent(long Generation, StudyWorkspaceRoute Route, Guid? SubjectId = null, Guid? LessonId = null);
internal sealed record StudyWorkspaceRow(string Id, string Title, string Detail, string OpenLabel, StudyWorkspaceIntent? Intent);
internal sealed record StudyWorkspaceSnapshot(long Generation, string Title, string Summary, string EmptyMessage,
    bool CanCreateSubject, bool UseCards, IReadOnlyList<StudyWorkspaceRow> Rows, StudyWorkspaceIntent NewSession, StudyWorkspaceIntent CreateSubject)
{
    internal bool Owns(StudyWorkspaceIntent intent) => intent.Generation == Generation &&
        (ReferenceEquals(intent, NewSession) || ReferenceEquals(intent, CreateSubject) || Rows.Any(row => ReferenceEquals(row.Intent, intent)));
}

// Authoring lives in StudyWorkspace.cui. This adapter provides typed values and
// delegates every command to the original Spaces session's serial work owner.
internal sealed class StudyWorkspaceView : UserControl, ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly CuiSceneHost _host = new();
    private readonly Func<StudyWorkspaceIntent, string, CancellationToken, Task> _execute;
    private readonly Func<bool> _current;
    private readonly Func<bool> _lifetime;
    private StudyWorkspaceSnapshot _snapshot;
    private string _subjectName = "";
    private Task<CuiSceneAvailability>? _show;
    internal StudyWorkspaceView(StudyWorkspaceSnapshot snapshot, Func<StudyWorkspaceIntent, string, CancellationToken, Task> execute, Func<bool> current, Func<bool> lifetime)
    { _snapshot = snapshot; _execute = execute; _current = current; _lifetime = lifetime; Content = _host; }
    public new event PropertyChangedEventHandler? PropertyChanged;
    internal string SubjectName => _subjectName;
    internal bool HasDraft => !string.IsNullOrWhiteSpace(_subjectName);
    internal long Generation => _snapshot.Generation;
    internal void ClearSavedDraft(string saved) { if (_subjectName == saved) _subjectName = ""; }
    internal void Update(StudyWorkspaceSnapshot snapshot) { _snapshot = snapshot; PropertyChanged?.Invoke(this, new(null)); }
    internal Task<CuiSceneAvailability> ShowAsync(ICuiSceneReadiness readiness, CancellationToken token)
    {
        if (_show is not null) return _show;
        using var stream = typeof(StudyWorkspaceView).Assembly.GetManifestResourceStream("Haven.Desktop.Study.Workspace.cui")
            ?? throw new InvalidOperationException("The authored Study workspace is unavailable.");
        using var reader = new StreamReader(stream);
        var document = new CuiRichParser().Parse(reader.ReadToEnd(), "StudyWorkspace.cui");
        return _show = _host.ShowAsync(new("study.workspace", "Study", "Study", document, this, this, readiness)
            { IsPublicationCurrent = _lifetime }, token);
    }
    internal Task CloseAsync() => _host.CloseOriginalAsync();
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "study.title" => _snapshot.Title, "study.summary" => _snapshot.Summary,
            "study.rows" => _snapshot.Rows, "study.cards" => _snapshot.UseCards, "study.empty" => _snapshot.Rows.Count == 0,
            "study.emptyMessage" => _snapshot.EmptyMessage, "study.canCreateSubject" => _snapshot.CanCreateSubject,
            "study.subjectName" => _subjectName, "study.newSession" => _snapshot.NewSession,
            "study.createSubject" => _snapshot.CreateSubject, _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path != "study.subjectName" || !_current()) return false;
        _subjectName = value as string ?? ""; PropertyChanged?.Invoke(this, new(path)); return true;
    }
    public bool? IsActionAvailable(string command) => command == "spaces.study.open" ? _current() : null;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        if (command != "spaces.study.open" || parameter is not StudyWorkspaceIntent intent)
            throw new InvalidOperationException("The Study action has no typed workspace target.");
        if (!_current() || !Owns(intent)) return ValueTask.CompletedTask;
        return new(_execute(intent, _subjectName, token));
    }
    internal bool Owns(StudyWorkspaceIntent intent) => _snapshot.Owns(intent);

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not StudyWorkspaceRow row || !_snapshot.Rows.Any(actual => ReferenceEquals(actual, row))) return false;
        value = path switch { "id" => row.Id, "title" => row.Title, "detail" => row.Detail,
            "openLabel" => row.OpenLabel, "intent" => row.Intent, "canOpen" => row.Intent is not null, _ => null };
        return value is not null;
    }
}

internal static class StudyWorkspaceProjection
{
    internal static StudyWorkspaceRow Topic(long generation, Haven.Core.ContainerDefinition subject, Haven.Core.Lesson lesson)
    {
        if (lesson.SubjectId != subject.Id) throw new InvalidOperationException("The Study topic does not belong to this subject.");
        var state = StudyLessonMetadata.Read(lesson);
        var reviewed = state.LastReviewedAt?.ToLocalTime().ToString("g") ?? "Never reviewed";
        return new(lesson.Id.ToString("N"), subject.Name + " · " + lesson.Name,
            $"{StudyLessonMetadata.RagLabel(state.Rag)} · {reviewed} · {state.Sessions.Count} recorded sessions · {state.TotalMinutes} minutes",
            "Start Study session", new(generation, StudyWorkspaceRoute.StartSession, subject.Id, lesson.Id));
    }
}

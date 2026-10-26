using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Study;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private StudyWorkspaceView? _studyPage;
    private long _studyGeneration;
    private long _studyPublishedEpoch;
    private bool IsStudyMountedCurrent() => !_closing && !_token.IsCancellationRequested && _studyPage is { } view &&
        _spacesPage?.Controller.State.Selection?.Id == SpaceRegistry.StudySpaceId &&
        ReferenceEquals(_spacesPage.SpaceWorkspaceContent, view) && _studyPublishedEpoch == _routeEpoch && view.Generation == _studyGeneration;
    private async Task WithStudyNavigationPreparationAsync(Func<Task> transition, CancellationToken token)
    {
        if (_studyPage?.HasDraft == true) throw new SpacesWorkspaceActionRefusal("Create the subject or clear its name before leaving Study.");
        await WithCurrentEditorPreparationAsync(async () =>
        {
            var chat = _chatPage;
            if (chat is not null) await chat.PrepareWorkspaceNavigationAsync();
            token.ThrowIfCancellationRequested(); DemandAlive();
            if (!ReferenceEquals(chat, _chatPage)) throw new SpacesWorkspaceActionRefusal("The current chat changed while its draft was saved.");
            await transition();
        }, token);
    }
    private sealed record StudyCommand(StudyWorkspaceView Owner, StudyWorkspaceIntent Intent, string SubjectName);
    private sealed record StudyAuthority(HistoryHomeAuthority Home, SpacesHomeSelection Selection, long Epoch);

    private async Task<StudyAuthority> ReadStudyAuthorityAsync(CancellationToken token)
    {
        try
        {
            var home = await ReadHistoryHomeAuthorityAsync(token);
            var selection = _spacesPage?.Controller.State.Selection;
            if (selection?.Id != SpaceRegistry.StudySpaceId)
                throw new SpacesWorkspaceActionRefusal("Select Study before opening its workspace.");
            var space = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(selection.Id, token);
            if (space is null || space.IsArchived || space.Kind != SpaceKind.Study)
                throw new SpacesWorkspaceActionRefusal("The selected Study Space is unavailable.");
            long epoch; lock (_gate) epoch = _routeEpoch;
            return new(home, selection, epoch);
        }
        catch (SpacesHistoryAuthorityRefusal refusal) { throw new SpacesWorkspaceActionRefusal(refusal.Message); }
    }
    private async Task DemandStudyAuthorityAsync(StudyAuthority captured, CancellationToken token)
    {
        var current = await ReadStudyAuthorityAsync(token);
        if (current.Home != captured.Home || !ReferenceEquals(current.Selection, captured.Selection) || current.Epoch != captured.Epoch)
            throw new SpacesWorkspaceActionRefusal("Study context changed; reopen the current Study workspace.");
    }

    private async Task<IReadOnlyList<SpacesHomeSidebarRow>> ReadStudySidebarAsync(SpacesHomeSelection selection, CancellationToken token)
    {
        if (selection.Id != SpaceRegistry.StudySpaceId) return [];
        var authority = await ReadStudyAuthorityAsync(token);
        var subjects = await _services.GetRequiredService<IContainerRepository>().GetByModeAsync(HavenMode.Study, token);
        await DemandStudyAuthorityAsync(authority, token);
        var rows = new List<SpacesHomeSidebarRow>
        {
            new("study:overview", "Study Home", "study", "Study", "overview", true),
            new("study:subjects", "Subjects", "folder", "Study", "subjects", true),
            new("study:progress", "My Progress", "chart", "Study", "progress", true),
            new("study:revision", "Revision Bank", "library", "Study", "revision", true),
            new("study:planner", "Planner", "calendar", "Study", "planner", true)
        };
        rows.AddRange(subjects.Where(subject => !subject.IsArchived).OrderBy(subject => subject.Name)
            .Select(subject => new SpacesHomeSidebarRow("study:subject:" + subject.Id.ToString("N"), subject.Name, "book", "Study", subject.Id.ToString("N"), true)));
        return rows;
    }

    private async Task OpenStudySidebarAsync(string id, CancellationToken token)
    {
        var route = id switch
        {
            "study:overview" => StudyWorkspaceRoute.Overview, "study:subjects" => StudyWorkspaceRoute.Subjects,
            "study:progress" => StudyWorkspaceRoute.Progress, "study:revision" => StudyWorkspaceRoute.RevisionBank,
            "study:planner" => StudyWorkspaceRoute.Planner, _ => StudyWorkspaceRoute.Subject
        };
        Guid? subject = null;
        if (route == StudyWorkspaceRoute.Subject)
        {
            if (!id.StartsWith("study:subject:", StringComparison.Ordinal) || !Guid.TryParseExact(id[14..], "N", out var actual))
                throw new SpacesWorkspaceActionRefusal("This Study sidebar destination is unavailable.");
            subject = actual;
        }
        await WithStudyNavigationPreparationAsync(() => OpenStudyWorkspaceAsync(route, subject, token), token);
    }

    private async Task OpenStudyWorkspaceAsync(StudyWorkspaceRoute route, Guid? subjectId, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var authority = await ReadStudyAuthorityAsync(token);
        var repository = _services.GetRequiredService<IContainerRepository>();
        var subjects = (await repository.GetByModeAsync(HavenMode.Study, token)).Where(item => !item.IsArchived).OrderBy(item => item.Name).ToArray();
        var selected = subjectId is { } id ? subjects.SingleOrDefault(item => item.Id == id) : null;
        if (subjectId is not null && selected is null) throw new SpacesWorkspaceActionRefusal("That Study subject is no longer available.");
        var generation = _studyGeneration + 1;
        var rows = new List<StudyWorkspaceRow>();
        var topicCount = 0;
        var recordedMinutes = 0;
        foreach (var subject in selected is not null ? new[] { selected } : subjects)
        {
            var lessons = await repository.GetLessonsAsync(subject.Id, token);
            topicCount += lessons.Count;
            recordedMinutes += lessons.Sum(lesson => StudyLessonMetadata.Read(lesson).TotalMinutes);
            if (route is StudyWorkspaceRoute.Overview or StudyWorkspaceRoute.Subjects)
                rows.Add(new(subject.Id.ToString("N"), subject.Name, $"{lessons.Count} topics", "Open subject", new(generation, StudyWorkspaceRoute.Subject, subject.Id)));
            else if (route is StudyWorkspaceRoute.Subject or StudyWorkspaceRoute.Progress)
            {
                foreach (var lesson in lessons.OrderBy(item => item.SortOrder))
                {
                    rows.Add(StudyWorkspaceProjection.Topic(generation, subject, lesson));
                }
            }
            else if (route == StudyWorkspaceRoute.RevisionBank)
            {
                var resources = await _services.GetRequiredService<IContainerResourceRepository>().GetByContainerAsync(subject.Id, token);
                rows.AddRange(resources.Select(resource => new StudyWorkspaceRow(resource.Id.ToString("N"), resource.Name,
                    subject.Name + " · " + resource.MediaType, "", null)));
            }
            else if (route == StudyWorkspaceRoute.Planner)
            {
                var assignments = await _services.GetRequiredService<IStudyPlannerService>().GetAssignmentsAsync(subject.Id, includeCompleted: true, token);
                rows.AddRange(assignments.Select(item => new StudyWorkspaceRow(item.PlanTaskId.ToString("N"), item.Task.Title,
                    subject.Name + " · " + item.Task.Status + " · " + (item.Task.DueAt?.ToLocalTime().ToString("g") ?? "No deadline"),
                    "Start linked Study session", new(generation, StudyWorkspaceRoute.StartSession, subject.Id, item.Link.LessonId))));
            }
        }
        await DemandStudyAuthorityAsync(authority, token);
        var title = route switch { StudyWorkspaceRoute.Subject => selected!.Name, StudyWorkspaceRoute.Progress => "My Progress",
            StudyWorkspaceRoute.RevisionBank => "Revision Bank", StudyWorkspaceRoute.Planner => "Study Planner", StudyWorkspaceRoute.Subjects => "Subjects", _ => "Study" };
        var summary = route switch { StudyWorkspaceRoute.Progress => "Recorded topic ratings and study sessions.",
            StudyWorkspaceRoute.RevisionBank => "Resources attached to your Study subjects.", StudyWorkspaceRoute.Planner => "Assignments linked to the same Planner tasks.",
            _ => $"{(selected is null ? subjects.Length : 1)} subjects · {topicCount} topics · {recordedMinutes} recorded study minutes" };
        var snapshot = new StudyWorkspaceSnapshot(generation, title, summary, "No saved items in this view yet.",
            route is StudyWorkspaceRoute.Overview or StudyWorkspaceRoute.Subjects,
            route is StudyWorkspaceRoute.Overview or StudyWorkspaceRoute.Subjects or StudyWorkspaceRoute.Planner, rows,
            new(generation, StudyWorkspaceRoute.StartSession, subjectId), new(generation, StudyWorkspaceRoute.CreateSubject));
        if (_studyPage is null)
        {
            var page = new StudyWorkspaceView(snapshot, ExecuteStudyCommandAsync, IsStudyMountedCurrent, () => !_closing && !_token.IsCancellationRequested && _spacesPage?.Controller.State.Selection?.Id == SpaceRegistry.StudySpaceId);
            _studyPage = page;
            var available = await page.ShowAsync(Haven.Desktop.Views.Pages.Spaces.SpacesHomePage.ImmediateReadinessInstance, token);
            if (available.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(available.Message);
        }
        await DemandStudyAuthorityAsync(authority, token);
        lock (_gate)
        {
            DemandAlive(); token.ThrowIfCancellationRequested();
            _studyGeneration = generation;
            if (_spacesPage is null || !_spacesPage.TryMountSpaceWorkspace(_studyPage)) throw new InvalidOperationException("The shared Study workspace mount is unavailable.");
            SetRootContent(_spacesPage);
            _publishedChatRoute = null; _chatPage = null;
            _publishedDetailsRoute = null; _publishedBuilderRoute = null; _publishedTaskRoute = null;
#if !ANDROID
            _publishedAssistantRoute = null;
#endif
            _routeEpoch++; _studyPublishedEpoch = _routeEpoch; UpdateAddress(SpaceRegistry.StudySpaceId);
            // Refresh only after the exact view, generation and route epoch are
            // mounted/published, so first-render actions observe current ownership.
            _studyPage.Update(snapshot);
        }
        PublishStateChanged();
    }

    private Task ExecuteStudyCommandAsync(StudyWorkspaceIntent intent, string subjectName, CancellationToken token)
    {
        lock (_gate)
        {
            DemandAlive(); token.ThrowIfCancellationRequested();
            if (_activeDetailsPreparation is not null)
                throw new SpacesWorkspaceActionRefusal("Finish the current close preparation before changing Study.");
            if (!IsStudyMountedCurrent() || _studyPage is null || !_studyPage.Owns(intent) || intent.Generation != _studyGeneration)
            {
                ReportWorkspaceActionRefusal("This Study action belongs to an older workspace.");
                return Task.CompletedTask;
            }
            var command = new StudyCommand(_studyPage, intent, subjectName);
            return AdmitWorkspaceDriver((start, execution) => ExecuteStudyDriverAsync(start, execution, command, token));
        }
    }
    private async Task ExecuteStudyDriverAsync(Task start, CommandExecution execution, StudyCommand command, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        await _commandSerial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await OnUiAsync(async () =>
            {
                var previous = ExecutingCommand.Value; ExecutingCommand.Value = execution; Volatile.Write(ref execution.Active, true);
                try
                {
                    try
                    {
                        var authority = await ReadStudyAuthorityAsync(linked.Token);
                        if (!IsStudyMountedCurrent() || !ReferenceEquals(_studyPage, command.Owner) || !command.Owner.Owns(command.Intent) || command.Intent.Generation != _studyGeneration)
                            throw new SpacesWorkspaceActionRefusal("This Study action is stale.");
                        if (command.Intent.Route == StudyWorkspaceRoute.CreateSubject)
                        {
                            var name = command.SubjectName.Trim();
                            if (name.Length is < 1 or > 120) throw new SpacesWorkspaceActionRefusal("Enter a subject name of 1–120 characters.");
                            await DemandStudyAuthorityAsync(authority, linked.Token);
                            var now = DateTimeOffset.UtcNow;
                            var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, name, null, "", "", now, now);
                            await _services.GetRequiredService<IContainerRepository>().CreateSubjectAsync(subject, linked.Token);
                            command.Owner.ClearSavedDraft(command.SubjectName);
                            await WithStudyNavigationPreparationAsync(() => OpenStudyWorkspaceAsync(StudyWorkspaceRoute.Subject, subject.Id, linked.Token), linked.Token);
                            await (_spacesPage?.RefreshSidebarItemsAsync(linked.Token) ?? Task.CompletedTask);
                        }
                        else if (command.Intent.Route == StudyWorkspaceRoute.StartSession)
                            await WithStudyNavigationPreparationAsync(() => StartStudySessionAsync(command.Intent.SubjectId, command.Intent.LessonId, linked.Token), linked.Token);
                        else await WithStudyNavigationPreparationAsync(() => OpenStudyWorkspaceAsync(command.Intent.Route, command.Intent.SubjectId, linked.Token), linked.Token);
                    }
                    catch (SpacesWorkspaceActionRefusal refusal) { ReportWorkspaceActionRefusal(refusal.Message); }
                }
                finally { ExecutingCommand.Value = previous; Volatile.Write(ref execution.Active, false); }
            });
        }
        finally { _commandSerial.Release(); }
    }

    private async Task StartStudySessionAsync(Guid? subjectId, Guid? lessonId, CancellationToken token)
    {
        var authority = await ReadStudyAuthorityAsync(token);
        var repository = _services.GetRequiredService<IContainerRepository>();
        if (subjectId is { } id)
        {
            var subjects = await repository.GetByModeAsync(HavenMode.Study, token);
            if (!subjects.Any(item => item.Id == id && !item.IsArchived)) throw new SpacesWorkspaceActionRefusal("The Study subject is unavailable.");
            if (lessonId is { } lesson && !(await repository.GetLessonsAsync(id, token)).Any(item => item.Id == lesson))
                throw new SpacesWorkspaceActionRefusal("The Study topic is unavailable.");
        }
        else if (lessonId is not null) throw new SpacesWorkspaceActionRefusal("A Study topic requires its subject.");
        await DemandStudyAuthorityAsync(authority, token);
        var page = await NewChatSessionFactory().CreateNewAsync(token);
        _ownedChats.Add(page);
        var published = false;
        try
        {
            await page.StartFreshConversationAsync(HavenMode.Study, subjectId, lessonId, SpaceRegistry.StudySpaceId);
            await DemandStudyAuthorityAsync(authority, token);
            lock (_gate)
            {
                DemandAlive(); token.ThrowIfCancellationRequested(); PublishChatPage(page);
                _chatsBySpace[SpaceRegistry.StudySpaceId] = page; ObserveConversationRoute(page); published = true;
            }
            PublishStateChanged(); await RefreshSpacesConversationsAsync(token);
        }
        finally
        {
            if (!published)
            {
#if !ANDROID
                UnwireChatAssistants(page);
#endif
                await page.DisposeAsync(); _ownedChats.Remove(page);
            }
        }
    }
}

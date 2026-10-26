using Haven.Core;
using Haven.Desktop.Views.Pages.Study;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class StudyWorkspaceProjectionTests
{
    [Fact]
    public void Unassessed_topic_stays_unrated_and_routes_to_same_canonical_subject_and_lesson()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "", "", now, now);
        var lesson = new Lesson(Guid.NewGuid(), subject.Id, "Algebra", "Quadratics", "{}", 0, now, now);
        var row = StudyWorkspaceProjection.Topic(7, subject, lesson);
        Assert.Contains("Not rated", row.Detail);
        Assert.Contains("Never reviewed", row.Detail);
        Assert.Contains("0 recorded sessions", row.Detail);
        Assert.DoesNotContain("%", row.Detail);
        Assert.Equal(subject.Id, row.Intent!.SubjectId);
        Assert.Equal(lesson.Id, row.Intent.LessonId);
        Assert.Equal(StudyWorkspaceRoute.StartSession, row.Intent.Route);
    }

    [Fact]
    public void Topic_projection_refuses_a_lesson_from_another_subject()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "", "", now, now);
        var lesson = new Lesson(Guid.NewGuid(), Guid.NewGuid(), "", "Foreign", "{}", 0, now, now);
        Assert.Throws<InvalidOperationException>(() => StudyWorkspaceProjection.Topic(1, subject, lesson));
    }

    [Fact]
    public void Workspace_action_requires_same_issued_object_and_current_generation()
    {
        var current = new StudyWorkspaceIntent(3, StudyWorkspaceRoute.StartSession, Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new StudyWorkspaceSnapshot(3, "Study", "", "", false, false,
            new[] { new StudyWorkspaceRow("topic", "Topic", "", "Start", current) },
            new(3, StudyWorkspaceRoute.StartSession), new(3, StudyWorkspaceRoute.CreateSubject));
        Assert.True(snapshot.Owns(current));
        Assert.False(snapshot.Owns(current with { }));
        Assert.False(snapshot.Owns(current with { Generation = 2 }));
        Assert.True(snapshot.Owns(snapshot.NewSession));
    }
}

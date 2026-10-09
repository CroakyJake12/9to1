using Haven.Application;
using Xunit;

namespace Haven.Application.Tests;

/// <summary>Same actual frame owner with the existing synthetic issued-lease harness;
/// these controls do not establish installed authority or generic cross-context self-join safety.</summary>
public sealed partial class TaskRunOriginalFrameOwnerTests
{
    [Fact]
    public async Task External_process_preflight_starts_no_work_and_leaves_original_frames_admissible()
    {
        var h = Harness.Create();
        ITaskRunOriginalFrameProcessJoinGuard guard = h.Runtime;
        var errors = new List<Exception>();
        try
        {
            guard.DemandExternalOriginalProcessJoin();
            Assert.Equal(0, h.Lease.Revalidations);
            Assert.Equal(0, h.Lease.Disposes);
            await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, CancellationToken.None);
            guard.DemandExternalOriginalProcessJoin();
            Assert.Equal("first", await h.Runtime.StartOriginalFrameAsync(h.Admission,
                _ => Task.FromResult("first"), CancellationToken.None));
            guard.DemandExternalOriginalProcessJoin();
            Assert.Equal("second", await h.Runtime.StartOriginalFrameAsync(h.Admission,
                _ => Task.FromResult("second"), CancellationToken.None));
            Assert.Equal(2, h.Lease.Revalidations);
            Assert.Equal(0, h.Lease.Disposes);
        }
        catch (Exception error) { errors.Add(error); }
        finally { await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, []); }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Live_original_frame_preflight_refuses_its_own_join_without_sealing_external_admission()
    {
        var h = Harness.Create();
        ITaskRunOriginalFrameProcessJoinGuard guard = h.Runtime;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        Task<string>? frame = null;
        try
        {
            await h.Runtime.RegisterOriginalAttemptAsync(h.Admission, CancellationToken.None);
            frame = h.Runtime.StartOriginalFrameAsync(h.Admission, async _ =>
            {
                var refusal = Assert.Throws<InvalidOperationException>(
                    (Action)(() => guard.DemandExternalOriginalProcessJoin()));
                Assert.Equal("A frame cannot join the owner that contains it.", refusal.Message);
                entered.TrySetResult();
                await release.Task;
                return "actual held frame";
            }, CancellationToken.None);
            await Task.WhenAny(entered.Task, frame).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            if (!entered.Task.IsCompleted) await frame;
            guard.DemandExternalOriginalProcessJoin();
            Assert.False(frame.IsCompleted);
            Assert.Equal(0, h.Lease.Disposes);
            release.TrySetResult();
            Assert.Equal("actual held frame", await frame);
            Assert.Equal("later frame", await h.Runtime.StartOriginalFrameAsync(h.Admission,
                _ => Task.FromResult("later frame"), CancellationToken.None));
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            release.TrySetResult();
            if (frame is not null) await CollectNewOriginalAsync(frame, errors, []);
            await CollectNewOriginalAsync(h.Runtime.CloseAndDrainAsync(), errors, []);
        }
        ThrowNewControlErrors(errors);
        Assert.Equal(1, h.Lease.Disposes);
    }
}

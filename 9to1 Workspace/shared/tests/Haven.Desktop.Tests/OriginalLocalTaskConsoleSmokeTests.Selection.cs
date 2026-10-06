using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_reopen_selection_suppresses_retained_or_late_initial_observation(bool acknowledgedBeforeOpen)
    {
        // These tuples are synthetic observations only. This real selector issues no
        // admission/Task/model authority and this control never calls Send or dispatch.
        var a = (TaskId: Guid.NewGuid(), ExecutionId: Guid.NewGuid(), ContextId: Guid.NewGuid());
        var b = (TaskId: Guid.NewGuid(), ExecutionId: Guid.NewGuid(), ContextId: Guid.NewGuid());
        var selector = new OriginalLocalTaskConsole.OriginalTaskSelection();
        var retainedA = selector.CreatePendingInitial();
        var readsA = 0;
        var readsB = 0;
        Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> observeA = () => { readsA++; return a; };
        Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> observeB = () => { readsB++; return b; };
        selector.SelectInitial(retainedA);
        if (acknowledgedBeforeOpen)
        {
            retainedA.CaptureObservation(observeA);
            Assert.Equal(a, selector.ObserveSelectedTuple());
        }
        selector.SelectAttachment(observeB); // Same publication point as a successful original Open.
        if (!acknowledgedBeforeOpen) retainedA.CaptureObservation(observeA);
        var historyTuple = selector.ObserveSelectedTuple();
        var discoveryTuple = selector.ObserveSelectedTuple();
        Assert.Equal(b, historyTuple);
        Assert.Equal(b, discoveryTuple);
        Assert.Equal(acknowledgedBeforeOpen ? 1 : 0, readsA);
        Assert.Equal(2, readsB);

        var refusedOpen = new IOException("The original attachment acquisition failed before publication.");
        var actualFailedAcquisition = Task.FromException<Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)>>(refusedOpen);
        Exception? caught = null;
        try { selector.SelectAttachment(await actualFailedAcquisition); }
        catch (Exception actual) { caught = actual; }
        Assert.Same(refusedOpen, caught);
        Assert.True(actualFailedAcquisition.IsFaulted);
        Assert.Contains(actualFailedAcquisition.Exception!.InnerExceptions, cause => ReferenceEquals(cause, refusedOpen));
        Assert.Equal(b, selector.ObserveSelectedTuple());
        Assert.Equal(acknowledgedBeforeOpen ? 1 : 0, readsA);
        Assert.Equal(3, readsB);
    }

    [Fact]
    public void Newly_selected_initial_without_acknowledgement_refuses_without_falling_back_to_old_attachment()
    {
        var selector = new OriginalLocalTaskConsole.OriginalTaskSelection();
        var readsOldAttachment = 0;
        selector.SelectAttachment(() => { readsOldAttachment++; return (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); });
        var selectedInitial = selector.CreatePendingInitial();
        selector.SelectInitial(selectedInitial);
        Assert.Throws<InvalidOperationException>(() => selector.ObserveSelectedTuple());
        Assert.Equal(0, readsOldAttachment);
        var unacknowledged = new InvalidOperationException("The actual selected initial source has not acknowledged a context.");
        selectedInitial.CaptureObservation(() => throw unacknowledged);
        Assert.Same(unacknowledged, Assert.Throws<InvalidOperationException>(() => selector.ObserveSelectedTuple()));
        Assert.Equal(0, readsOldAttachment);
    }
}

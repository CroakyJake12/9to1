namespace NineToOne.Web.Spaces;

/// <summary>Retains the original operation and presentation failures without letting
/// later cleanup replace an uncertain mutation's evidence.</summary>
internal sealed class BrowserSpacesFailures
{
    private readonly List<Exception> _failures = [];
    private readonly HashSet<Exception> _observed = new(ReferenceEqualityComparer.Instance);
    private bool _onlyCancellation = true;
    public bool HasFailures => _failures.Count != 0;

    public void Retain(Exception error, Task? actual = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (actual?.Exception is { } original)
        {
            _onlyCancellation = false;
            RetainLeaves(original);
        }
        else
        {
            // A synchronously thrown OCE or faulted Task is not cancellation.
            _onlyCancellation &= actual?.IsCanceled == true && error is OperationCanceledException;
            RetainLeaves(error);
        }
    }

    public async Task ObserveAsync(Func<Task> source)
    {
        Task? actual = null;
        try { actual = source(); await actual; }
        catch (Exception error) { Retain(error, actual); }
    }

    public void ThrowIfAny()
    {
        if (_failures.Count == 0) return;
        if (_failures.Count == 1 && (_onlyCancellation || _failures[0] is not OperationCanceledException))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_failures[0]).Throw();
        throw new AggregateException(_failures);
    }

    private void RetainLeaves(Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
            foreach (var inner in group.InnerExceptions) RetainLeaves(inner);
        else if (_observed.Add(error)) _failures.Add(error);
    }
}

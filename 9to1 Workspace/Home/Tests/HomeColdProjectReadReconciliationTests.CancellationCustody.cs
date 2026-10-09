using System.Reflection;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed partial class HomeColdProjectReadReconciliationTests
{
    [Fact]
    public Task Actual_raw_cancellation_cannot_erase_an_independent_synchronous_source_OCE()
        => WithSource(async (source, _, _) =>
        {
            // No input or grant is issued. Exercise the actual private Home Context
            // callback and independently joined raw cancellation occurrence directly.
            var foreign = new OperationCanceledException("independent original scope failure");
            var type = typeof(HomeColdProjectReadReconciliation).GetNestedType("Context", BindingFlags.NonPublic)!;
            var context = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, [source, null, (Action<Action>)(body => { body(); throw foreign; }), (Action<Task>)(_ => { }), false], null)!;
            var scope = type.GetMethod("Scope", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var callback = Assert.Throws<TargetInvocationException>(() =>
            {
                _ = scope.Invoke(context, [(Action)(() => { })]);
            });
            var callbackGroup = Assert.IsType<AggregateException>(callback.InnerException);
            Assert.Contains(callbackGroup.InnerExceptions, cause => ReferenceEquals(cause, foreign));

            using var cancellation = new CancellationTokenSource();
            var actualRaw = Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            cancellation.Cancel();
            var awaitRaw = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "Await" && !method.IsGenericMethod);
            var actualObservation = (Task)awaitRaw.Invoke(context, [actualRaw])!;
            var originalCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actualObservation);
            Assert.True(actualRaw.IsCanceled); Assert.True(actualObservation.IsCanceled);
            var settle = type.GetMethod("Settle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var actualSettlement = (Task)settle.Invoke(context, [originalCancellation])!;
            var failure = await Assert.ThrowsAsync<AggregateException>(() => actualSettlement);
            Assert.True(actualSettlement.IsFaulted); Assert.False(actualSettlement.IsCanceled);
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, foreign));
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, originalCancellation));
            // Both exact tasks are terminal and independently observed before CTS disposal.
            Assert.True(actualRaw.IsCompleted); Assert.True(actualObservation.IsCompleted);
        });
}

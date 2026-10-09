using System.Reflection;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual command source publication/role components. This controlled composition supplies
// no private current Files/native positive or Home approval witness.
public sealed partial class HomeColdProjectReadReconciliationTests
{
    [Fact]
    public async Task Command_READ_retention_cannot_join_the_actual_pending_owner_close()
    {
        await WithSource(async (source, factoryCause, known) =>
        {
            var callbacks = 0;
            var actual = source.AcquireOriginalCommandReadWithinSourceAsync(null!, null!, "no public reference grant", body => body(), _ =>
            {
                callbacks++;
                Assert.Throws<InvalidOperationException>(() => { _ = source.CloseAndDrainOriginalAsync(); });
                Assert.Null(typeof(HomeColdProjectReadReconciliation).GetField("_close", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
            }, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.Equal(1, callbacks);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, factoryCause)); known.Add(factoryCause);
        });
    }
    [Fact]
    public async Task Command_READ_synchronous_publication_OCE_remains_faulted_and_retained()
    {
        await WithSource(async (source, _, known) =>
        {
            var cause = new OperationCanceledException("actual command READ publication OCE"); known.Add(cause);
            var actual = source.AcquireOriginalCommandReadWithinSourceAsync(null!, null!, "no grant", body => body(), _ => throw cause, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, cause));
        });
    }
    [Fact]
    public async Task Command_READ_swallowed_repeat_preserves_refusal_before_any_current_selection_factory()
    {
        await WithSource(async (source, factoryCause, known) =>
        {
            Exception? refusal = null;
            var actual = source.AcquireOriginalCommandReadWithinSourceAsync(null!, null!, "no grant", body =>
            {
                body(); try { body(); } catch (Exception cause) { refusal = cause; known.Add(cause); }
            }, _ => { }, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.IsType<InvalidOperationException>(refusal);
            Assert.Contains(Leaves(error), value => ReferenceEquals(value, refusal));
            Assert.DoesNotContain(Leaves(error), value => ReferenceEquals(value, factoryCause));
        });
    }
    [Fact]
    public async Task Private_command_role_cannot_be_cast_into_initial_Chat_input_issuance()
    {
        await WithSource((source, _, _) =>
        {
            var work = typeof(HomeColdProjectReadReconciliation).GetMethod("ReserveAdmitted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, [null])!;
            var commandType = typeof(HomeColdProjectReadReconciliation).GetNestedType("CommandRead", BindingFlags.NonPublic)!;
            var command = Activator.CreateInstance(commandType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [source, work], null)!;
            work.GetType().GetField("_command", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(work, command);
            var input = Assert.IsAssignableFrom<ITaskRunColdOriginalProjectInput>(work);
            Assert.False(source.IsOwnedOriginalProjectInput(input)); Assert.False(source.IsIssuedOriginalProjectInput(input));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = source.ValidateOriginalProjectInputWithinSourceAsync(input, null!, body => body(), _ => { }, CancellationToken.None); });
            var read = Assert.IsAssignableFrom<IDeveloperOriginalProjectCommandRead>(command);
            Assert.False(source.IsOwnedOriginalCommandRead(read)); Assert.False(source.IsIssuedOriginalCommandRead(read));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = source.CaptureOriginalCommandNativeReadWithinSourceAsync(read, body => body(), _ => { }, CancellationToken.None); });
            return Task.CompletedTask;
        });
    }
}

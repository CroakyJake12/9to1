using System.Runtime.ExceptionServices;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CanonicalSqliteOriginalSourceScopeCallbackClassificationTests
{
    [Fact]
    public async Task Actual_body_occurrence_in_a_repeated_reference_DAG_is_not_a_foreign_callback_failure()
    {
        var bodyCause = new IOException("actual body occurrence"); Exception wrapper = bodyCause;
        for (var depth = 0; depth < 30; depth++) wrapper = new AggregateException(wrapper, wrapper);
        await ObserveActualScope(bodyCause, wrapper, unexpected: false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Unknown_opaque_empty_or_over_bound_envelopes_keep_the_actual_occurrence_and_block_productive_body(int shape)
    {
        var bodyCause = new IOException("actual body occurrence"); Exception wrapper;
        if (shape == 0)
        {
            wrapper = new AggregateException(bodyCause, new IOException("foreign sibling"));
            for (var depth = 0; depth < 80; depth++) wrapper = new AggregateException(wrapper, wrapper);
        }
        else if (shape == 1)
        {
            wrapper = bodyCause;
            for (var depth = 0; depth < 80; depth++) wrapper = new AggregateException(wrapper);
        }
        else if (shape == 2) wrapper = new AggregateException();
        else if (shape == 3) wrapper = new OpaqueAggregate(bodyCause);
        else wrapper = new AggregateException(Enumerable.Repeat<Exception>(bodyCause, 4097));
        await ObserveActualScope(bodyCause, wrapper, unexpected: true);
    }
    private sealed class OpaqueAggregate(Exception inner) : AggregateException(inner)
    {
        public override string Message => throw new InvalidOperationException("The source proof must not call an opaque virtual getter.");
    }
    private static async Task ObserveActualScope(Exception bodyCause, Exception wrapper, bool unexpected)
    {
        var retained = new List<Exception>(); var productiveEffects = 0;
        var scope = new CanonicalSqliteOriginalSourceScope(new object(), actual =>
        {
            try { actual(); }
            catch (Exception observed)
            {
                Assert.Same(bodyCause, observed);
                ExceptionDispatchInfo.Capture(wrapper).Throw();
            }
        }, _ => { }, retained.Add, () =>
        {
            if (retained.Count != 0) ExceptionDispatchInfo.Capture(retained[0]).Throw();
        });
        var observedCaller = Record.Exception(() => { scope.Run(() => throw bodyCause); });
        Assert.Same(wrapper, observedCaller);
        if (unexpected)
        {
            Assert.Same(wrapper, Assert.Single(retained));
            var refused = Record.Exception(() => { scope.RunProductive(() => productiveEffects++); });
            Assert.Same(wrapper, refused); Assert.Equal(0, productiveEffects);
        }
        else Assert.Empty(retained);
        // Independently settle this SAME real scope. The full original wrapper is
        // retained as an exact object; no formatting, Flatten or type-only waiver.
        var joining = scope.JoinAllAsync();
        var joined = await Record.ExceptionAsync(async () => await joining);
        Assert.NotNull(joined);
        var roots = Assert.IsType<AggregateException>(joined).InnerExceptions;
        Assert.NotEmpty(roots);
        Assert.All(roots, cause => Assert.True(ReferenceEquals(cause, wrapper) || ReferenceEquals(cause, bodyCause)));
        Assert.Contains(roots, cause => ReferenceEquals(cause, wrapper));
    }
}

using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Several tests use the shared in-memory board store and clear it during setup.
/// Keep those tests out of parallel execution so one test cannot erase another's
/// just-saved board between save and reopen.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BoardSessionTestCollection
{
    public const string Name = "Board session store";
}

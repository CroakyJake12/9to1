using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces;

/// <summary>
/// The first HavenOS-owned Spaces destinations. The host keeps shell routing at the OS root;
/// this app surface only selects existing Haven modes and Space records.
/// </summary>
public enum SpacesDestination
{
    Home,
    Chat,
    Study,
    Tasks,
    Agent,
    Shopping,
    Research,
    Translate,
    Experiences
}

public sealed record SpacesNavigationItem(
    SpacesDestination Destination,
    string Label,
    string IconKey);

/// <summary>
/// Adapter implemented by the HavenOS shell. Existing shell/app launch services remain the source
/// of truth for how a mode or configured Space is actually presented.
/// </summary>
public interface ISpacesNavigationHost
{
    Task OpenHomeAsync(CancellationToken cancellationToken = default);
    Task OpenModeAsync(HavenMode mode, CancellationToken cancellationToken = default);
    Task OpenSpaceAsync(SpaceDefinition space, CancellationToken cancellationToken = default);
}

/// <summary>
/// Platform-neutral navigation surface for the HavenOS Spaces app.
/// </summary>
public sealed class SpacesAppSurface
{
    private static readonly IReadOnlyList<SpacesNavigationItem> NavigationItems = Array.AsReadOnly<SpacesNavigationItem>(
    [
        new(SpacesDestination.Home, "Home", "home"),
        new(SpacesDestination.Chat, "Chat", "chat"),
        new(SpacesDestination.Study, "Study", "book"),
        new(SpacesDestination.Tasks, "Tasks", "tasks"),
        new(SpacesDestination.Agent, "Agent", "agents"),
        new(SpacesDestination.Shopping, "Shopping", "cart"),
        new(SpacesDestination.Research, "Research", "search"),
        new(SpacesDestination.Translate, "Translate", "translate"),
        new(SpacesDestination.Experiences, "Experiences", "experiences")
    ]);

    private readonly SpaceRegistry _spaces;
    private readonly ISpacesNavigationHost _host;
    private readonly SemaphoreSlim _navigationGate = new(1, 1);

    public SpacesAppSurface(SpaceRegistry spaces, ISpacesNavigationHost host)
    {
        _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public static IReadOnlyList<SpacesNavigationItem> Navigation => NavigationItems;

    public SpacesDestination CurrentDestination { get; private set; } = SpacesDestination.Home;

    public event Action<SpacesDestination>? DestinationChanged;

    public async Task NavigateAsync(
        SpacesDestination destination,
        CancellationToken cancellationToken = default)
    {
        await _navigationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (destination)
            {
                case SpacesDestination.Home:
                    await _host.OpenHomeAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Chat:
                    await NavigateWithScopeAsync(
                        nextSpaceId: SpaceRegistry.ChatSpaceId,
                        token => _host.OpenModeAsync(HavenMode.Chat, token),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Study:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.StudySpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Tasks:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.TasksSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Agent:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.AgentSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Shopping:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.ShoppingSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Research:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.ResearchSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Translate:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.TranslateSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                case SpacesDestination.Experiences:
                    await NavigateToBuiltInSpaceAsync(SpaceRegistry.ExperiencesSpaceId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown Spaces destination.");
            }

            CurrentDestination = destination;
            DestinationChanged?.Invoke(destination);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private async Task NavigateToBuiltInSpaceAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        var space = await _spaces.GetAsync(spaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Required built-in Space '{spaceId}' is unavailable.");

        await NavigateWithScopeAsync(
            space.Id,
            token => _host.OpenSpaceAsync(space, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task NavigateWithScopeAsync(
        Guid? nextSpaceId,
        Func<CancellationToken, Task> launch,
        CancellationToken cancellationToken)
    {
        var previousSpaceId = await _spaces.GetCurrentSpaceIdAsync(cancellationToken).ConfigureAwait(false);
        var scopeChanged = previousSpaceId != nextSpaceId;

        if (scopeChanged)
            await _spaces.SetCurrentSpaceIdAsync(nextSpaceId, cancellationToken).ConfigureAwait(false);

        try
        {
            await launch(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception launchError)
        {
            if (!scopeChanged)
                throw;

            try
            {
                await _spaces.SetCurrentSpaceIdAsync(previousSpaceId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Spaces navigation failed and the previous Space scope could not be restored.",
                    launchError,
                    rollbackError);
            }

            throw;
        }
    }
}

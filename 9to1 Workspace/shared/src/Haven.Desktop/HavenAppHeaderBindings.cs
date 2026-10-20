#if !ANDROID
using System.ComponentModel;
using CakeOS.Cui;
using Haven.Application.Haven;

namespace Haven.Desktop;

/// <summary>
/// Trusted typed host-context projection for the reusable canonical shared app-header component
/// (<c>Views/Pages/Home/HavenAppHeader.cui</c>). Embedded surfaces expose search only — model,
/// notifications and profile belong to the persistent Browse/Haven rail and are unified across
/// apps. Standalone surfaces expose all four through the SAME real Home gateway/owners. The header
/// never duplicates per-app model/account stores and never fabricates availability: every flag is
/// derived from the actual <see cref="IHavenHostContext"/> the real host supplied.
/// </summary>
public sealed class HavenAppHeaderBindings(
    IHavenHostContext hostContext,
    Func<string, CancellationToken, Task<HavenSharedOpenResult>> openSharedAsync,
    Func<string, CancellationToken, Task> searchAsync)
    : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    private readonly IHavenHostContext _host = hostContext;
    private readonly Func<string, CancellationToken, Task<HavenSharedOpenResult>> _open = openSharedAsync;
    private readonly Func<string, CancellationToken, Task> _search = searchAsync;
    private string _searchText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True when this surface is hosted inside the Haven/Browse frame.</summary>
    public bool IsEmbedded => _host.IsEmbedded;

    /// <summary>The header always exposes search.</summary>
    public bool ShowSearch => true;

    /// <summary>Embedded headers keep model selection on the persistent Haven rail; only
    /// standalone headers expose it.</summary>
    public bool ShowModel => !_host.IsEmbedded && _host.ModelSupported;

    /// <summary>Embedded headers keep notifications on the persistent Haven rail; only
    /// standalone headers expose them.</summary>
    public bool ShowNotifications => !_host.IsEmbedded && _host.NotificationsSupported;

    /// <summary>Embedded headers keep profile on the persistent Haven rail; only standalone
    /// headers expose it.</summary>
    public bool ShowProfile => !_host.IsEmbedded && _host.ProfileSupported;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
        }
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "IsEmbedded" => IsEmbedded,
            "ShowSearch" => ShowSearch,
            "ShowModel" => ShowModel,
            "ShowNotifications" => ShowNotifications,
            "ShowProfile" => ShowProfile,
            "SearchText" => SearchText,
            _ => null
        };
        return value is not null;
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        => command switch
        {
            "9to1.Haven.OpenModel" => OpenShared("9to1.Haven.OpenModel", cancellationToken),
            "9to1.Haven.OpenNotifications" => OpenShared("9to1.Haven.OpenNotifications", cancellationToken),
            "9to1.Haven.OpenProfile" => OpenShared("9to1.Haven.OpenProfile", cancellationToken),
            "9to1.Haven.Search" => Search(parameter as string ?? SearchText, cancellationToken),
            _ => ValueTask.CompletedTask
        };

    private async ValueTask OpenShared(string kind, CancellationToken token)
    {
        if (_open is not null) await _open(kind, token);
    }

    private async ValueTask Search(string query, CancellationToken token)
    {
        if (_search is not null) await _search(query, token);
    }
}
#endif

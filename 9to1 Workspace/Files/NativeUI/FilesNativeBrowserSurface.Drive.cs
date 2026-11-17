using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

// Google Drive as an authorised remote provider for the canonical Files surface
// (v3 module "Files and ten organisational folder views": local and authorised
// remote providers, provider-native operations with truthful availability).
// The connection is the owner's own Google account through the app's PKCE
// OAuth browser flow — the same platform pattern as Calendar and Mail. Drive
// rows are canonical HostedItemMetadata over the Drive provider identity;
// browsing, folder creation, rename, recoverable Trash and restore operate the
// real remote content. Nothing here touches the local workspace store.
public sealed partial class FilesNativeBrowserSurface
{
    private static readonly Guid DriveStoreId = new("6f1c4a72-9e3b-4c60-8d5f-2a7c50e1b9d3");
    private static readonly HttpClient DriveHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
    private const string DriveScope = "https://www.googleapis.com/auth/drive";
    private const int DrivePageSize = 200;

    private sealed record DriveAccount(string RefreshToken, string Scope, DateTimeOffset ConnectedAt);
    private sealed record DriveFolder(string GoogleId, string Name)
    { public static DriveFolder Root { get; } = new("", "Google Drive"); }

    private readonly List<DriveFolder> _drivePath = [];
    private readonly List<List<DriveFolder>> _driveBack = [];
    private DriveAccount? _driveAccount;
    private bool _driveAccountLoaded;
    private DateTimeOffset _driveTokenExpiry;

    private bool InDrive => _page?.StoreID == DriveStoreId;
    private static bool IsDriveCommand(string command) =>
        command is "9to1.Files.Drive" or "9to1.Files.Drive.Connect" or "9to1.Files.Drive.Disconnect"
            or "9to1.Files.Drive.Server" or "9to1.Files.Drive.SelfHosted";
    private string DriveAccountPath => Path.Combine(
        Environment.GetEnvironmentVariable("HAVEN_DATA_DIR") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "9to1-development", "profile"),
        "google-drive-account.json");

    private static string DriveClientId() =>
        (Environment.GetEnvironmentVariable("HAVEN_GOOGLE_DRIVE_CLIENT_ID")?.Trim() is { Length: > 10 } fromEnvironment
            ? fromEnvironment
            : File.Exists(DriveClientFilePath()) ? File.ReadAllText(DriveClientFilePath()).Trim() : "") is { Length: > 10 } id ? id : "";
    private static string DriveClientFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "9to1", "google-drive-client-id.txt");
    // Console-created web-type clients carry a secret; desktop-type clients don't.
    // The handshake adapts to either so the owner never has to recreate a client.
    private static string? DriveClientSecret()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "9to1", "google-drive-client-secret.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() is { Length: > 4 } secret ? secret : null : null;
    }
    private static void DriveApplyClientSecret(Dictionary<string, string> form)
    { if (DriveClientSecret() is { } secret) form["client_secret"] = secret; }

    private bool DriveActionAvailable(string command) => command switch
    {
        // A local Drive sign-in already stored in this profile opens without a new consent.
        "9to1.Files.Drive" => Available && _pendingMutation is null,
        "9to1.Files.Drive.Connect" => Available && _pendingMutation is null,
        "9to1.Files.Drive.Disconnect" => Available && _pendingMutation is null && _driveAccount is not null,
        // Truthful availability (V3-FILES-FEA-01): the chooser lists every provider kind;
        // the ones not wired in this build answer honestly when selected.
        "9to1.Files.Drive.Server" or "9to1.Files.Drive.SelfHosted" => Available && _pendingMutation is null,
        _ => false,
    };

    // In Drive mode the shared browser commands operate the remote provider; the
    // local mutation review, move chooser, sharing and tab commands never apply.
    private bool? DriveInPageActionAvailable(string command) => command switch
    {
        "9to1.Files.Home" or "9to1.Files.Trash" or "9to1.Files.Refresh" or "9to1.Files.Search" => Available,
        "9to1.Files.OpenSidebarFolder" => Available,
        "9to1.Files.Up" => Available && _drivePath.Count > 1,
        "9to1.Files.Back" => Available && _driveBack.Count > 0,
        "9to1.Files.Forward" => false,
        "9to1.Files.More" => false,
        "9to1.Files.Open" => Available && _items.SelectedItem is HostedItemMetadata row && _page!.Items.Any(item => ReferenceEquals(item, row)),
        "9to1.Files.CreateFolder" => Available && !string.IsNullOrWhiteSpace(_editName.Text) && _page is { IsTrash: false },
        "9to1.Files.Rename" => Available && !string.IsNullOrWhiteSpace(_editName.Text) && _page is { IsTrash: false }
            && _items.SelectedItem is HostedItemMetadata renamed && _page.Items.Any(item => ReferenceEquals(item, renamed)),
        "9to1.Files.Delete" => Available && _page is { IsTrash: false } && _items.SelectedItem is HostedItemMetadata trashed
            && _page.Items.Any(item => ReferenceEquals(item, trashed)),
        "9to1.Files.Restore" => Available && _page is { IsTrash: true } && _items.SelectedItem is HostedItemMetadata restored
            && _page.Items.Any(item => ReferenceEquals(item, restored)),
        "9to1.Files.TechnicalDetails" => Available && _items.SelectedItem is HostedItemMetadata details
            && _page!.Items.Any(item => ReferenceEquals(item, details)),
        _ => false,
    };

    private bool TryGetDriveValue(string path, out object? value)
    {
        switch (path)
        {
            case "DriveConnectVisible": value = !_driveAccountLoaded || _driveAccount is null; return true;
            case "DriveConnected": value = _driveAccountLoaded && _driveAccount is not null; return true;
            case "CanDrive": value = DriveActionAvailable("9to1.Files.Drive") && (_driveAccountLoaded ? _driveAccount is not null : File.Exists(DriveAccountPath)); return true;
            case "CanDriveConnect": value = DriveActionAvailable("9to1.Files.Drive.Connect"); return true;
            default: value = null; return false;
        }
    }

    private async Task InvokeDriveAsync(string command, object? parameter, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        EnsureDriveAccountLoaded();
        try
        {
            if (command == "9to1.Files.Drive.Server")
            { _status = "Server drives (Cloudflare R2, S3-compatible) will connect here; that provider is not wired in this build yet, so nothing was changed. Google Drive connects now."; return; }
            if (command == "9to1.Files.Drive.SelfHosted")
            { _status = "Self-hosted drives (WebDAV) will connect here; that provider is not wired in this build yet, so nothing was changed. Google Drive connects now."; return; }
            if (command == "9to1.Files.Drive.Connect")
            {
                // The chooser's Google Drive entry reopens an already-connected drive
                // instead of asking for consent again.
                if (_driveAccount is not null) { _drivePath.Clear(); _drivePath.Add(DriveFolder.Root); _driveBack.Clear(); _query = ""; _search.Text = ""; await DrivePublishAsync(false, token); return; }
                await DriveConnectAsync(token); return;
            }
            if (command == "9to1.Files.Drive.Disconnect")
            {
                _accessToken = null;
                File.Delete(DriveAccountPath); _driveAccount = null;
                _drivePath.Clear(); _driveBack.Clear();
                if (InDrive) { _page = null; _originalPageCurrent = null; _items.ItemsSource = null; _items.SelectedItem = null; }
                _status = "Google Drive disconnected. Your Drive files and authorisation remain untouched.";
                Changed(); return;
            }
            if (_driveAccount is null) { _status = "Connect Google Drive first (the browser will ask for permission once)."; return; }
            if (command == "9to1.Files.Drive") { _drivePath.Clear(); _drivePath.Add(DriveFolder.Root); _driveBack.Clear(); _query = ""; _search.Text = ""; await DrivePublishAsync(false, token); return; }

            // Commands below only reach here while the displayed page is the Drive provider.
            switch (command)
            {
                case "9to1.Files.Home":
                    _drivePath.Clear(); _driveBack.Clear(); _page = null; _items.ItemsSource = null;
                    await InvokeAsync("9to1.Files.Home", token); return;
                case "9to1.Files.Trash":
                    _query = ""; _search.Text = ""; await DrivePublishAsync(true, token); return;
                case "9to1.Files.Refresh":
                    await DrivePublishAsync(_page?.IsTrash == true, token); return;
                case "9to1.Files.Search":
                    _query = _search.Text ?? ""; await DrivePublishAsync(_page?.IsTrash == true, token); return;
                case "9to1.Files.Up":
                    PushDriveBack(); _drivePath.RemoveAt(_drivePath.Count - 1);
                    _query = ""; _search.Text = ""; await DrivePublishAsync(false, token); return;
                case "9to1.Files.Back":
                    var restored = _driveBack[^1]; _driveBack.RemoveAt(_driveBack.Count - 1);
                    _drivePath.Clear(); _drivePath.AddRange(restored);
                    _query = ""; _search.Text = ""; await DrivePublishAsync(_page?.IsTrash == true, token); return;
                case "9to1.Files.OpenSidebarFolder":
                    if (parameter is HostedItemMetadata sidebarFolder && sidebarFolder.Kind == HostedItemKind.Folder &&
                        _page?.Items.Any(item => ReferenceEquals(item, sidebarFolder)) == true)
                    { PushDriveBack(); _drivePath.Add(new DriveFolder(DriveGoogleId(sidebarFolder), sidebarFolder.Name));
                      _query = ""; _search.Text = ""; await DrivePublishAsync(false, token); }
                    return;
                case "9to1.Files.Open":
                    if (_items.SelectedItem is HostedItemMetadata opened)
                    {
                        if (opened.Kind == HostedItemKind.Folder)
                        { PushDriveBack(); _drivePath.Add(new DriveFolder(DriveGoogleId(opened), opened.Name));
                          _query = ""; _search.Text = ""; await DrivePublishAsync(false, token); }
                        else await DriveOpenFileAsync(opened, token);
                    }
                    return;
                case "9to1.Files.CreateFolder":
                {
                    var name = (_editName.Text ?? "").Trim();
                    if (name.Length == 0 || name.Length > 242) { _status = "Enter a folder name up to 242 characters."; return; }
                    var created = await DriveJsonAsync(HttpMethod.Post, "/files", JsonSerializer.Serialize(new
                    {
                        name, mimeType = "application/vnd.google-apps.folder", parents = new[] { CurrentDriveFolder.GoogleId },
                    }), token);
                    _status = created.ValueKind is JsonValueKind.Undefined ? "Google Drive could not create the folder."
                        : $"Created ‘{name}’ in Google Drive.";
                    _editName.Text = ""; await DrivePublishAsync(false, token); return;
                }
                case "9to1.Files.Rename":
                {
                    var name = (_editName.Text ?? "").Trim();
                    if (_items.SelectedItem is not HostedItemMetadata target || name.Length == 0 || name.Length > 242) { _status = "Select an item and enter a new name first."; return; }
                    await DriveJsonAsync(HttpMethod.Patch, "/files/" + DriveGoogleId(target), JsonSerializer.Serialize(new { name }), token);
                    _status = $"Renamed to ‘{name}’ in Google Drive."; _editName.Text = "";
                    await DrivePublishAsync(_page?.IsTrash == true, token); return;
                }
                case "9to1.Files.Delete":
                {
                    if (_items.SelectedItem is not HostedItemMetadata target) { _status = "Select an item first."; return; }
                    await DriveJsonAsync(HttpMethod.Patch, "/files/" + DriveGoogleId(target) + "?fields=id,trashed",
                        JsonSerializer.Serialize(new { trashed = true }), token);
                    _status = $"Moved ‘{target.Name}’ to Google Drive Trash. Open Trash to restore it.";
                    await DrivePublishAsync(false, token); return;
                }
                case "9to1.Files.Restore":
                {
                    if (_items.SelectedItem is not HostedItemMetadata target) { _status = "Select a deleted item first."; return; }
                    await DriveJsonAsync(HttpMethod.Patch, "/files/" + DriveGoogleId(target) + "?fields=id,trashed",
                        JsonSerializer.Serialize(new { trashed = false }), token);
                    _status = $"Restored ‘{target.Name}’ from Google Drive Trash.";
                    await DrivePublishAsync(true, token); return;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || token.IsCancellationRequested)
        { _status = "The Google Drive operation was cancelled."; Changed(); }
        catch (Exception error)
        {
            _status = "Google Drive: " + DriveErrorText(error);
            Changed();
        }
    }

    private void PushDriveBack()
    {
        _driveBack.Add([.. _drivePath]);
        if (_driveBack.Count > 64) _driveBack.RemoveAt(0);
    }
    private DriveFolder CurrentDriveFolder => _drivePath.Count > 0 ? _drivePath[^1] : DriveFolder.Root;
    private static string DriveGoogleId(HostedItemMetadata item) => item.ContentHash ?? throw new InvalidOperationException("The Drive row has no provider identity.");
    // The canonical Guid is presentation identity only; the provider id travels
    // in ContentHash so Drive operations can never be aimed at local store rows.
    private static HostedItemId DriveItemId(string googleId) => new(DriveGuid(googleId));
    private static Guid DriveGuid(string googleId) => new(SHA1.HashData(Encoding.UTF8.GetBytes("9to1-drive:" + googleId)).AsSpan(0..16).ToArray());

    private async Task DrivePublishAsync(bool trash, CancellationToken token)
    {
        var folder = CurrentDriveFolder;
        // Trash is a flat provider view in Drive; browsing scopes to the current folder.
        var clauses = new List<string> { trash ? "trashed = true" :
            folder.GoogleId.Length == 0 ? "'root' in parents and trashed = false" : $"'{folder.GoogleId.Replace("'", "")}' in parents and trashed = false" };
        var query = _query.Trim();
        if (query.Length > 0) clauses.Add($"name contains '{query.Replace("'", "")}'");
        var url = "/files?q=" + Uri.EscapeDataString(string.Join(" and ", clauses)) +
            "&fields=" + Uri.EscapeDataString("files(id,name,mimeType,size,createdTime,modifiedTime,parents),nextPageToken") +
            "&pageSize=" + DrivePageSize + "&orderBy=name&supportsAllDrives=true";
        using var document = await DriveGetDocumentAsync(url, token);
        var items = new List<HostedItemMetadata>();
        if (document.RootElement.TryGetProperty("files", out var files))
            foreach (var row in files.EnumerateArray())
            {
                var googleId = row.GetProperty("id").GetString() ?? "";
                var mimeType = row.GetProperty("mimeType").GetString() ?? "application/octet-stream";
                if (mimeType == "application/vnd.google-apps.shortcut") continue;
                var name = row.GetProperty("name").GetString() ?? "Untitled";
                var isFolder = mimeType == "application/vnd.google-apps.folder";
                long? size = row.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed) ? parsed : null;
                var created = row.TryGetProperty("createdTime", out var createdElement) && DateTimeOffset.TryParse(createdElement.GetString(), out var createdValue) ? createdValue : DateTimeOffset.UtcNow;
                var modified = row.TryGetProperty("modifiedTime", out var modifiedElement) && DateTimeOffset.TryParse(modifiedElement.GetString(), out var modifiedValue) ? modifiedValue : created;
                items.Add(new HostedItemMetadata(DriveItemId(googleId), new FilesLocationId(DriveStoreId),
                    folder.GoogleId.Length == 0 ? null : DriveItemId(folder.GoogleId), name,
                    isFolder ? HostedItemKind.Folder : HostedItemKind.File, mimeType,
                    "Google account", trash ? "Google Drive Trash" : "Google Drive", size, created, modified,
                    null, SyncAvailability.CloudOnly, false, googleId));
            }
        _page = new FilesNativeBrowserPage(DriveStoreId, "google-drive-v1",
            folder.GoogleId.Length == 0 ? null : DriveItemId(folder.GoogleId).Value, items, null, trash,
            trash ? "Google Drive — Trash" : folder.GoogleId.Length == 0 ? "Google Drive" : "Google Drive — " + folder.Name);
        _items.SelectedItem = null;
        ApplyDisplayedNameSort();
        _status = items.Count == 0 ? (string.IsNullOrWhiteSpace(query) ? "No items here." : $"No Drive items match ‘{query}’ in this folder.")
            : $"{items.Count} items" + (string.IsNullOrWhiteSpace(query) ? "" : $" matching ‘{query}’") + (trash ? " in Drive Trash" : $" in {folder.Name}");
        Changed();
    }

    private async Task DriveOpenFileAsync(HostedItemMetadata item, CancellationToken token)
    {
        _status = $"Downloading ‘{item.Name}’ from Google Drive…"; Changed();
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files/" + Uri.EscapeDataString(DriveGoogleId(item)) + "?alt=media&supportsAllDrives=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await DriveAccessTokenAsync(token));
        using var response = await DriveHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) { _status = $"Google Drive could not open ‘{item.Name}’ ({(int)response.StatusCode})."; Changed(); return; }
        var safeName = string.Join("_", item.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (safeName.Length == 0) safeName = "drive-item";
        var path = Path.Combine(Path.GetTempPath(), "9to1-drive", DriveGuid(DriveGoogleId(item)).ToString("N") + "-" + safeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            await response.Content.CopyToAsync(target, token);
        _status = $"Downloaded ‘{item.Name}’ from Google Drive. Opening with the system default application…"; Changed();
        try { Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = false, CreateNoWindow = true }); }
        catch (Exception error) { _status = $"‘{item.Name}’ is saved at {path}, but no system application opened it: {error.Message}"; }
    }

    private void EnsureDriveAccountLoaded()
    {
        // A credential created after startup (the usual first-connect order: the app
        // offers Connect, the owner consents in the browser, the token lands later)
        // must be picked up without restarting Files, so an empty cache is re-checked
        // against the file every time rather than latched to "not connected".
        if (_driveAccountLoaded && _driveAccount is not null) return;
        _driveAccountLoaded = true;
        if (!File.Exists(DriveAccountPath)) { _driveAccount = null; return; }
        try
        {
            if (File.Exists(DriveAccountPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(DriveAccountPath));
                var root = document.RootElement;
                _driveAccount = new DriveAccount(root.GetProperty("refreshToken").GetString() ?? "",
                    root.GetProperty("scope").GetString() ?? DriveScope,
                    DateTimeOffset.TryParse(root.TryGetProperty("connectedAt", out var at) ? at.GetString() : null, out var connected) ? connected : DateTimeOffset.UtcNow);
                if (_driveAccount.RefreshToken.Length == 0) _driveAccount = null;
            }
        }
        catch (Exception error) { AddOriginalFailure(error); _driveAccount = null; }
    }

    private async Task DriveConnectAsync(CancellationToken token)
    {
        var clientId = DriveClientId();
        if (clientId.Length == 0)
        {
            _status = "Google Drive needs an OAuth client id. Create a desktop client in your Google Cloud project, " +
                      "then set HAVEN_GOOGLE_DRIVE_CLIENT_ID or save it in ~/.config/9to1/google-drive-client-id.txt.";
            Changed(); return;
        }
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var authorize = "https://accounts.google.com/o/oauth2/v2/auth?" + QueryString(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
                ["scope"] = DriveScope, ["access_type"] = "offline", ["include_granted_scopes"] = "true",
                ["prompt"] = "consent", ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
            });
            _status = "Opening your browser for one-time Google Drive permission…"; Changed();
            // Some sandboxed desktop sessions cannot launch a browser from inside apps;
            // the same link is always left in the private temp file so consent can proceed
            // by opening it manually. The listener is identical either way.
            try { File.WriteAllText("/tmp/9to1-drive-auth-url.txt", authorize); } catch (IOException) { }
            try { Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true }); }
            catch (Exception) { _status = "No browser could be opened from Files. Open the link saved at /tmp/9to1-drive-auth-url.txt, then return here."; Changed(); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var code = await WaitForDriveCodeAsync(listener, state, timeout.Token);
            var exchangeForm = new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["code"] = code,
                ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
            };
            DriveApplyClientSecret(exchangeForm);
            var tokenResponse = await DriveHttp.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(exchangeForm), timeout.Token);
            var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(timeout.Token)).RootElement;
            if (!tokenResponse.IsSuccessStatusCode || !json.TryGetProperty("refresh_token", out var refresh) ||
                refresh.GetString() is not { Length: > 0 } issuedToken)
            { _status = "Google Drive sign-in did not return a lasting permission. Try once more."; Changed(); return; }
            var account = new DriveAccount(issuedToken, DriveScope, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.GetDirectoryName(DriveAccountPath)!);
            File.WriteAllText(DriveAccountPath, JsonSerializer.Serialize(new { refreshToken = account.RefreshToken, scope = account.Scope, connectedAt = account.ConnectedAt }));
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(DriveAccountPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _driveAccount = account; _accessToken = null;
            _status = "Google Drive connected."; Changed();
            _drivePath.Clear(); _drivePath.Add(DriveFolder.Root); _driveBack.Clear();
            await DrivePublishAsync(false, token);
        }
        finally { listener.Stop(); }
    }
    private string? _accessToken;

    private static async Task<string> WaitForDriveCodeAsync(TcpListener listener, string state, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
        var stream = client.GetStream();
        var buffer = new byte[16384];
        var received = await stream.ReadAsync(buffer, token);
        var request = Encoding.ASCII.GetString(buffer, 0, received);
        var line = request.Split('\n', '\r')[0];
        var parts = line.Split(' ');
        var target = parts.Length > 1 ? parts[1] : "/";
        var queryStart = target.IndexOf('?');
        var values = queryStart < 0 ? new Dictionary<string, string>() :
            target[(queryStart + 1)..].Split('&').Select(pair => pair.Split('=', 2))
                .Where(kv => kv.Length == 2).ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv[1]));
        var body = values.TryGetValue("state", out var got) && got == state && values.TryGetValue("code", out var code)
            ? "<!doctype html><meta charset=utf-8><title>9to1</title><body style='font-family:sans-serif;padding:2em'>Connected. Return to 9to1 Files.</body>"
            : "<!doctype html><meta charset=utf-8><title>9to1</title><body style='font-family:sans-serif;padding:2em'>Sign-in did not complete. Return to 9to1 Files.</body>";
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response, token);
        await stream.FlushAsync(token);
        if (values.TryGetValue("state", out var stateSeen) && stateSeen == state && values.TryGetValue("code", out var issued) && issued.Length > 0) return issued;
        throw new InvalidOperationException("The browser did not return the Drive permission code.");
    }

    private async Task<string> DriveAccessTokenAsync(CancellationToken token)
    {
        EnsureDriveAccountLoaded();
        if (_driveAccount is null) throw new InvalidOperationException("Google Drive is not connected.");
        if (_accessToken is not null && DateTimeOffset.UtcNow < _driveTokenExpiry) return _accessToken;
        var clientId = DriveClientId();
        if (clientId.Length == 0) throw new InvalidOperationException("The Google OAuth client id is no longer configured.");
        var refreshForm = new Dictionary<string, string> { ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = _driveAccount.RefreshToken };
        DriveApplyClientSecret(refreshForm);
        var response = await DriveHttp.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(refreshForm), token);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement;
        if (!response.IsSuccessStatusCode || json.GetProperty("access_token").GetString() is not { Length: > 0 } issued)
            throw new InvalidOperationException("Google refused the saved Drive permission; reconnect in Files.");
        _accessToken = issued;
        _driveTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, json.GetProperty("expires_in").GetInt32() - 120));
        return issued;
    }

    private async Task<JsonDocument> DriveGetDocumentAsync(string relativeUrl, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3" + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await DriveAccessTokenAsync(token));
        using var response = await DriveHttp.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(DriveErrorText(body, (int)response.StatusCode));
        return JsonDocument.Parse(body);
    }

    private async Task<JsonElement> DriveJsonAsync(HttpMethod method, string relativeUrl, string json, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, "https://www.googleapis.com/drive/v3" + relativeUrl)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await DriveAccessTokenAsync(token));
        using var response = await DriveHttp.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(DriveErrorText(body, (int)response.StatusCode));
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string DriveErrorText(string body, int status)
    {
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                return text.Length > 160 ? text[..160] : text;
        }
        catch (JsonException) { }
        return $"request refused ({status}).";
    }

    private static string QueryString(Dictionary<string, string> values) =>
        string.Join('&', values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

    private static string DriveErrorText(Exception error) => error is InvalidOperationException or HttpRequestException or JsonException
        ? (error.Message.Length > 200 ? error.Message[..200] : error.Message)
        : "unexpected failure. Check the Drive connection and try again.";
}

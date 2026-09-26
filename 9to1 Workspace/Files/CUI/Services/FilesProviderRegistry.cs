namespace HavenOS.Files;

/// <summary>Validates and indexes capability-declaring Files providers for a CUI application session.</summary>
public sealed class FilesProviderRegistry
{
	private readonly IReadOnlyList<IFilesProvider> _providers;
	private readonly IReadOnlyDictionary<FilesLocationId, IFilesProvider> _byLocation;

	public FilesProviderRegistry(IEnumerable<IFilesProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(providers);
		IFilesProvider[] items = providers.ToArray();
		if (items.Any(static provider => provider is null))
			throw new ArgumentException("Files provider registrations cannot contain null entries.", nameof(providers));
		if (items.Any(static provider => string.IsNullOrWhiteSpace(provider.Location.ProviderId) || string.IsNullOrWhiteSpace(provider.Location.Name)))
			throw new ArgumentException("Each Files provider must declare a provider ID and location name.", nameof(providers));
		if (items.Any(static provider => provider.Location.Id.Value == Guid.Empty))
			throw new ArgumentException("Each Files location must declare a stable non-empty ID.", nameof(providers));
		if (items.Select(static provider => provider.Location.Id).Distinct().Count() != items.Length)
			throw new ArgumentException("Files provider location IDs must be unique.", nameof(providers));

		_providers = items.OrderBy(static provider => provider.Location.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(static provider => provider.Location.Id.ToString(), StringComparer.Ordinal).ToArray();
		_byLocation = _providers.ToDictionary(static provider => provider.Location.Id);
	}

	public FilesPage<FilesLocation> ListLocations(string? pageToken = null, int pageSize = 100)
	{
		if (pageSize is < 1 or > 500)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		int offset = ParsePageToken(pageToken);
		if (offset > _providers.Count)
			throw new ArgumentException("The Files location page token is invalid.", nameof(pageToken));
		FilesLocation[] page = _providers.Skip(offset).Take(pageSize).Select(static provider => provider.Location).ToArray();
		int nextOffset = offset + page.Length;
		return new FilesPage<FilesLocation>(page, nextOffset < _providers.Count ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
	}

	public FilesResult<FilesProviderCapabilities> GetCapabilities(FilesLocationId locationId)
	{
		if (!_byLocation.TryGetValue(locationId, out IFilesProvider? provider))
			return FilesResult<FilesProviderCapabilities>.Failure(new FilesError(
				FilesErrorCode.ItemNotFound, "The Files location is not registered.", "Files.GetProviderCapabilities",
				locationId.ToString(), false, false));
		return FilesResult<FilesProviderCapabilities>.Success(provider.Location.Capabilities);
	}

	public FilesResult<IFilesProvider> GetProvider(FilesLocationId locationId)
	{
		if (!_byLocation.TryGetValue(locationId, out IFilesProvider? provider))
			return FilesResult<IFilesProvider>.Failure(new FilesError(
				FilesErrorCode.ItemNotFound, "The Files location is not registered.", "Files.ResolveProvider",
				locationId.ToString(), false, false));
		if (!provider.Location.IsAvailable)
			return FilesResult<IFilesProvider>.Failure(new FilesError(
				FilesErrorCode.ProviderUnavailable,
				provider.Location.UnavailableReason ?? "The Files location is currently unavailable.",
				"Files.ResolveProvider", locationId.ToString(), true, true));
		return FilesResult<IFilesProvider>.Success(provider);
	}

	public FilesResult<bool> RequireCapability(FilesLocationId locationId, FilesProviderCapabilities capability, string action)
	{
		FilesResult<IFilesProvider> resolved = GetProvider(locationId);
		if (!resolved.IsSuccess)
			return FilesResult<bool>.Failure(resolved.Error!);
		return resolved.Value!.Location.Capabilities.Supports(capability)
			? FilesResult<bool>.Success(true)
			: FilesProviderCapabilityExtensions.Unsupported<bool>(resolved.Value.Location, action);
	}

	private static int ParsePageToken(string? token)
	{
		if (token is null)
			return 0;
		if (int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int offset) && offset >= 0)
			return offset;
		throw new ArgumentException("The Files location page token is invalid.", nameof(token));
	}
}

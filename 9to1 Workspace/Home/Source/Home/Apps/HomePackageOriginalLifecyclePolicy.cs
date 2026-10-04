using System.Collections.Frozen;

namespace HavenOS.Home.Apps;

// Registered by the SAME trusted installer composition for exact privately issued
// selections. These classifications and supported actions are planning policy,
// never a signature, installed receipt, permission grant or platform effect.
internal sealed record HomePackageOriginalLifecycleRegistration(
    HomePackageArtifactSelection OriginalSelection,
    HomePackageComponentClass Classification,
    IReadOnlySet<HomePackageAction> SupportedActions);

internal sealed class HomePackageOriginalLifecyclePolicy : IHomePackageOriginalLifecyclePolicy
{
    private sealed record Registration(HomePackageComponentClass Classification,
        FrozenSet<HomePackageAction> SupportedActions);

    private readonly IHomePackageOriginalPlatformOwner _owner;
    private readonly FrozenDictionary<HomePackageArtifactSelection, Registration> _registrations;
    public string OriginalHomePackageId { get; }
    public string Platform { get; }
    public string Abi { get; }

    internal HomePackageOriginalLifecyclePolicy(IHomePackageOriginalPlatformOwner sameOriginalOwner,
        string originalHomePackageId, string originalPlatform, string originalAbi,
        IEnumerable<HomePackageOriginalLifecycleRegistration> originalRegistrations)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalOwner);
        ArgumentNullException.ThrowIfNull(originalRegistrations);
        if (!HomePackageArtifactSelection.Identifier(originalHomePackageId) ||
            !HomePackageArtifactSelection.Identifier(originalPlatform) ||
            !HomePackageArtifactSelection.Identifier(originalAbi))
            throw new ArgumentException("The original registered Home/platform/ABI tuple is required.");
        _owner = sameOriginalOwner;
        OriginalHomePackageId = originalHomePackageId;
        Platform = originalPlatform;
        Abi = originalAbi;
        var captured = new Dictionary<HomePackageArtifactSelection, Registration>(ReferenceEqualityComparer.Instance);
        var packages = new HashSet<string>(StringComparer.Ordinal);
        // Detach every owner mapping and action set synchronously, before publication or any await.
        foreach (var row in originalRegistrations)
        {
            if (captured.Count >= 256 || row is null || row.OriginalSelection is null ||
                !row.OriginalSelection.IssuedBy(_owner) || !Enum.IsDefined(row.Classification) ||
                row.OriginalSelection.Descriptor.Platform != Platform ||
                row.OriginalSelection.Descriptor.Abi != Abi ||
                row.OriginalSelection.Descriptor.ComponentClass != DeclaredComponentClass(row.Classification) ||
                row.SupportedActions is null ||
                !packages.Add(row.OriginalSelection.Descriptor.PackageId))
                throw new InvalidDataException("Unique bounded SAME-owner platform selections and classifications are required.");
            var actions = row.SupportedActions.Take(8).ToArray();
            if (actions.Length > 7 || actions.Any(action => !Enum.IsDefined(action)) ||
                actions.Distinct().Count() != actions.Length)
                throw new InvalidDataException("The original supported action set is invalid.");
            if (actions.Any(action => action is not (HomePackageAction.Install or HomePackageAction.Update or
                HomePackageAction.Repair or HomePackageAction.Rollback or HomePackageAction.Uninstall)))
                throw new InvalidDataException("Only implemented lifecycle actions may be registered.");
            if (row.Classification == HomePackageComponentClass.MandatorySharedCore &&
                actions.Contains(HomePackageAction.Uninstall))
                throw new InvalidDataException("Mandatory shared core cannot be registered for removal.");
            captured.Add(row.OriginalSelection, new(row.Classification, actions.ToFrozenSet()));
        }
        if (!captured.Any(row => row.Key.Descriptor.PackageId == OriginalHomePackageId &&
            row.Value.Classification == HomePackageComponentClass.MandatorySharedCore))
            throw new InvalidDataException("The exact original Home selection must be registered as mandatory shared core.");
        _registrations = captured.ToFrozenDictionary(ReferenceEqualityComparer.Instance);
    }

    // Version1 native signed-descriptor values. Owner registration must agree
    // with the declaration already retained inside the SAME original selection.
    // An arbitrary legacy label is not silently promoted to a modularity class.
    internal static string DeclaredComponentClass(HomePackageComponentClass classification) => classification switch {
        HomePackageComponentClass.MandatorySharedCore => "mandatory-shared-core",
        HomePackageComponentClass.OptionalApp => "optional-app",
        HomePackageComponentClass.AppRequiredDependency => "app-required-dependency",
        HomePackageComponentClass.OptionalFeature => "optional-feature-dependency",
        _ => throw new InvalidDataException("The original component classification is unsupported.")
    };

    public HomePackageComponentClass? ClassifyOriginal(HomePackageArtifactSelection originalSelection) =>
        Retained(originalSelection, out var row) ? row!.Classification : null;

    public bool SupportsOriginalAction(HomePackageArtifactSelection originalSelection, HomePackageAction action) =>
        Enum.IsDefined(action) && Retained(originalSelection, out var row) && row!.SupportedActions.Contains(action);

    private bool Retained(HomePackageArtifactSelection? originalSelection, out Registration? row)
    {
        row = null;
        return originalSelection is not null && originalSelection.IssuedBy(_owner) &&
            originalSelection.Descriptor.Platform == Platform && originalSelection.Descriptor.Abi == Abi &&
            _registrations.TryGetValue(originalSelection, out row);
    }

    // The explicitly selected version policy is SemVer 2.0.0. Build metadata does
    // not change precedence. Opaque product versions, partial versions, wildcards,
    // coercion and alternate range syntaxes remain unsupported, never guessed.
    public bool TrySatisfyOriginalVersion(string actualVersion, HomePackageDependency requiredRange, out bool satisfies)
    {
        satisfies = false;
        if (requiredRange is null || !HomePackageArtifactSelection.Identifier(requiredRange.PackageId) ||
            !TryParse(actualVersion, out var actual)) return false;
        ParsedVersion? minimum = null, maximum = null;
        if (requiredRange.MinimumVersion is { } low && !TryParse(low, out minimum)) return false;
        if (requiredRange.MaximumVersionExclusive is { } high && !TryParse(high, out maximum)) return false;
        if (minimum is not null && maximum is not null && Compare(minimum, maximum) >= 0) return false;
        satisfies = (minimum is null || Compare(actual!, minimum) >= 0) &&
            (maximum is null || Compare(actual!, maximum) < 0);
        return true;
    }

    private sealed record ParsedVersion(string[] Core, string[]? Prerelease);

    private static bool TryParse(string? value, out ParsedVersion? version)
    {
        version = null;
        if (value is null || !HomePackageArtifactSelection.Text(value, 128) || value != value.Trim()) return false;
        var buildOffset = value.IndexOf('+');
        if (buildOffset >= 0)
        {
            if (value.IndexOf('+', buildOffset + 1) >= 0 || !Identifiers(value[(buildOffset + 1)..], false, out _))
                return false;
            value = value[..buildOffset];
        }
        var prereleaseOffset = value.IndexOf('-');
        string[]? prerelease = null;
        if (prereleaseOffset >= 0)
        {
            if (!Identifiers(value[(prereleaseOffset + 1)..], true, out prerelease)) return false;
            value = value[..prereleaseOffset];
        }
        var core = value.Split('.');
        if (core.Length != 3 || core.Any(part => !CanonicalNumber(part))) return false;
        version = new(core, prerelease);
        return true;
    }

    private static bool Identifiers(string value, bool prerelease, out string[]? identifiers)
    {
        identifiers = value.Split('.');
        if (identifiers.Any(part => part.Length == 0 ||
            part.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            prerelease && part.All(char.IsAsciiDigit) && !CanonicalNumber(part)))
        { identifiers = null; return false; }
        return true;
    }

    private static bool CanonicalNumber(string value) => value.Length != 0 &&
        (value.Length == 1 || value[0] != '0') && value.All(char.IsAsciiDigit);

    private static int Numeric(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0 ? length : StringComparer.Ordinal.Compare(left, right);
    }

    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        for (var index = 0; index < 3; index++)
        {
            var core = Numeric(left.Core[index], right.Core[index]);
            if (core != 0) return core;
        }
        if (left.Prerelease is null) return right.Prerelease is null ? 0 : 1;
        if (right.Prerelease is null) return -1;
        for (var index = 0; index < Math.Min(left.Prerelease.Length, right.Prerelease.Length); index++)
        {
            var a = left.Prerelease[index]; var b = right.Prerelease[index];
            var aNumber = a.All(char.IsAsciiDigit); var bNumber = b.All(char.IsAsciiDigit);
            var item = aNumber && bNumber ? Numeric(a, b) :
                aNumber != bNumber ? aNumber ? -1 : 1 : StringComparer.Ordinal.Compare(a, b);
            if (item != 0) return item;
        }
        return left.Prerelease.Length.CompareTo(right.Prerelease.Length);
    }
}

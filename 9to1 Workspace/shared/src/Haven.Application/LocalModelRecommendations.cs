using System.Globalization;

namespace Haven.Application;

/// <summary>Read-only device evidence. Unknown memory is null, never zero or a guessed GPU.</summary>
public sealed record LocalModelGpu(string Name, long? TotalBytes, long? AvailableBytes);
public sealed record LocalModelHardware(DateTimeOffset ObservedAt, string Architecture, int LogicalProcessors,
    long? TotalRamBytes, long? AvailableRamBytes, IReadOnlyList<LocalModelGpu> Gpus, string? Limitation = null);
public interface ILocalModelHardwareProbe
{
    Task<LocalModelHardware> ReadAsync(CancellationToken cancellationToken);
}

public enum LocalModelMemoryFit { Unknown, TooLarge, Cpu, Gpu }
public sealed record LocalModelFit(LocalModelMemoryFit Kind, long EstimatedBytes, string Explanation);
public sealed record LocalModelRecommendation(HuggingFaceModelDetails Repository, HuggingFaceModelFile File,
    LocalModelFit Fit, long Downloads)
{
    public string Key => Repository.RepositoryId + "@" + Repository.CommitSha + ":" + File.Path;
    public string Name => Repository.RepositoryId.Split('/').Last();
    public string Publisher => Repository.RepositoryId.Split('/')[0];
    public string Format => File.Path.Split('/').Last();
    public string DownloadSize => LocalModelMemoryEstimator.FormatBytes(File.Size) + " download";
    public string FitLabel => Fit.Kind == LocalModelMemoryFit.Gpu ? "Fits GPU memory estimate" : "Fits RAM estimate";
}

/// <summary>Conservative, explicitly labelled file-size estimate, not a runtime/quality benchmark.
/// GPU memory is never added across cards or added to RAM. Context/KV cache and backend support
/// still need the canonical runtime's load-time check.</summary>
public static class LocalModelMemoryEstimator
{
    public const long GiB = 1024L * 1024 * 1024;

    public static LocalModelFit Estimate(long fileBytes, LocalModelHardware hardware)
    {
        if (fileBytes <= 0 || fileBytes > (long.MaxValue - GiB) / 2)
            return new(LocalModelMemoryFit.Unknown, 0, "Model file size is unavailable.");
        var estimate = checked(fileBytes + fileBytes / 4 + GiB);
        var ramBudget = Budget(hardware.TotalRamBytes, hardware.AvailableRamBytes, 2 * GiB);
        var gpu = hardware.Gpus.FirstOrDefault(item => Budget(item.TotalBytes, item.AvailableBytes, GiB) >= estimate);
        // A GPU-sized file still requires enough host memory to load/stage it.
        if (ramBudget >= estimate && gpu is not null)
            return new(LocalModelMemoryFit.Gpu, estimate,
                $"About {FormatBytes(estimate)} including estimated overhead; fits available RAM and {gpu.Name} memory with headroom. GPU backend and context support are checked by Dulche at load time.");
        if (ramBudget >= estimate)
            return new(LocalModelMemoryFit.Cpu, estimate,
                $"About {FormatBytes(estimate)} including estimated overhead; fits available RAM with 2 GiB reserved. CPU execution may be slower; context and architecture support are checked at load time.");
        return ramBudget is null
            ? new(LocalModelMemoryFit.Unknown, estimate, "Available RAM could not be measured. Hardware fit is unknown.")
            : new(LocalModelMemoryFit.TooLarge, estimate, $"About {FormatBytes(estimate)} is needed by this estimate, exceeding available RAM after reserving 2 GiB.");
    }

    private static long? Budget(long? total, long? available, long reserve) =>
        total is > 0 && available is >= 0 && available <= total ? Math.Max(0, available.Value - reserve) : null;

    public static string FormatBytes(long bytes) => (bytes / (double)GiB).ToString("0.#", CultureInfo.InvariantCulture) + " GiB";

    public static IReadOnlyList<LocalModelRecommendation> Rank(LocalModelHardware hardware,
        IEnumerable<(HuggingFaceModelSummary Summary, HuggingFaceModelDetails Details)> repositories, int limit = 3)
    {
        if (limit is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(limit));
        return repositories.SelectMany(item => item.Details.Files
                .Where(file => file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) &&
                    !file.Path.Contains("mmproj", StringComparison.OrdinalIgnoreCase) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(file.Path, @"-\d{5}-of-\d{5}", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                .Select(file => new LocalModelRecommendation(item.Details, file, Estimate(file.Size, hardware), item.Summary.Downloads))
                .Where(item => item.Fit.Kind is LocalModelMemoryFit.Gpu or LocalModelMemoryFit.Cpu)
                .OrderByDescending(item => item.Fit.Kind)
                .ThenByDescending(item => item.File.Path.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.File.Size)
                .Take(1))
            .DistinctBy(item => item.Repository.RepositoryId, StringComparer.Ordinal)
            .OrderByDescending(item => item.Fit.Kind)
            .ThenByDescending(item => item.Downloads)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(limit).ToArray();
    }
}

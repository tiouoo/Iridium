using Iridium.Download;
using Iridium.Minecraft;

namespace Iridium.Installation;

/// <summary>
/// Shared state for one install execution. Carries the target Minecraft context, the
/// download source and the shared <see cref="DefaultDownloader"/> so every download
/// operation shares a single global concurrency budget.
/// </summary>
public sealed class InstallContext {
    public required MinecraftContext Minecraft { get; init; }
    public required DownloadSource Source { get; init; }

    /// <summary>Shared downloader owned by the executor; all operations must reuse it.</summary>
    public DefaultDownloader? Downloader { get; internal set; }

    internal string CurrentOperationKey { get; set; } = string.Empty;
    internal Action<string, double>? ProgressReporter { get; set; }

    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);

    public void SetState(string key, object? value) => _state[key] = value;

    public T? GetState<T>(string key) =>
        _state.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// Reports sub-progress (0..1) of the currently executing operation, e.g. downloaded
    /// items over total items.
    /// </summary>
    public void ReportProgress(double operationProgress = 0d)
        => ProgressReporter?.Invoke(CurrentOperationKey, operationProgress);
}

/// <summary>Progress snapshot aggregated by the <see cref="InstallTaskExecutor"/>.</summary>
public sealed record InstallProgress {
    public required string CurrentOperation { get; init; }
    public double TotalProgress { get; init; }
    public int CompletedOperations { get; init; }
    public int TotalOperations { get; init; }
}

/// <summary>Result of executing an <see cref="InstallTask"/>.</summary>
public sealed record InstallResult {
    public required MinecraftContext Minecraft { get; init; }
    public IReadOnlyList<Exception> Failures { get; init; } = [];
    public TimeSpan Elapsed { get; init; }
    public bool IsSuccess => Failures.Count == 0;
}

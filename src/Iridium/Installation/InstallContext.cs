using Iridium.Download;
using Iridium.Interfaces;
using Iridium.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Installation;

/// <summary>
/// Shared state for one install execution: the install target, the download source and the
/// data bag steps exchange intermediate results through. Execution infrastructure (step
/// scheduler, per-step download concurrency) is owned by the <see cref="InstallTaskExecutor"/>
/// for the duration of the execution and is not part of this context's public surface.
/// </summary>
public sealed class InstallContext {
    public required MinecraftTarget Target { get; init; }
    public required DownloadSource Source { get; init; }

    /// <summary>Shared default downloader; steps obtain downloads via <see cref="CreateResourceDownloader"/>.</summary>
    internal DefaultDownloader Downloader { get; set; } = DefaultDownloader.Default;

    /// <summary>Per-step download concurrency for this execution, set by the executor.</summary>
    internal int DownloadConcurrency { get; set; } = 32;

    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);

    public void SetState(string key, object? value) => _state[key] = value;

    public T? GetState<T>(string key) =>
        _state.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// Creates a <see cref="ResourceDownloader"/> bound to the shared default downloader and
    /// this execution's per-step download concurrency, so every download step applies the same
    /// uniform concurrency limit.
    /// </summary>
    internal ResourceDownloader CreateResourceDownloader(IMinecraftLayout layout) =>
        new(Downloader, Source, layout, DownloadConcurrency);
}

/// <summary>Result of executing an <see cref="InstallTask"/>.</summary>
public sealed record InstallResult {
    public required MinecraftTarget Target { get; init; }
    public IReadOnlyList<Exception> Failures { get; init; } = [];
    public TimeSpan Elapsed { get; init; }
    public bool IsSuccess => Failures.Count == 0;
}

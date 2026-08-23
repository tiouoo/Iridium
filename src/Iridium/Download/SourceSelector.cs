using System.Collections.Concurrent;
using System.Diagnostics;
using Flurl.Http;
using Iridium.Enums;
using Iridium.Interfaces.Resources;

namespace Iridium.Download;

/// <summary>
/// Global download-source selection for Iridium.
///
/// Modes:
/// <list type="bullet">
/// <item><see cref="SourceSelectionMode.Auto"/> — latency-probes each candidate once
/// (cached per host for <see cref="ProbeTtl"/>) and orders by measured speed.</item>
/// <item><see cref="SourceSelectionMode.OfficialPreferred"/>/<see cref="SourceSelectionMode.MirrorPreferred"/>
/// — orders the two candidates by preference.</item>
/// <item><see cref="SourceSelectionMode.OfficialOnly"/> — never touches a mirror and skips probing.</item>
/// </list>
///
/// Probing is deliberately cheap and rare: one HEAD per host, cached, single-flight, so bulk
/// downloads never re-measure per file. Actual request timeouts fall back to the other
/// candidate, alternating up to <see cref="MaxAttempts"/>.
/// </summary>
public static class SourceSelector {
    private static readonly ConcurrentDictionary<string, CachedProbe> ProbeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim ProbeLock = new(1, 1);

    private static SourceSelectionMode _mode = SourceSelectionMode.Auto;
    private static TimeSpan _probeTimeout = TimeSpan.FromSeconds(3);
    private static TimeSpan _probeTtl = TimeSpan.FromMinutes(5);
    private static int _maxAttempts = 4;

    public static SourceSelectionMode Mode => _mode;

    public static TimeSpan ProbeTimeout => _probeTimeout;

    public static TimeSpan ProbeTtl => _probeTtl;

    public static int MaxAttempts => _maxAttempts;

    /// <summary>Mirror used for game-file downloads (libraries/assets). Defaults to BMCLAPI.</summary>
    public static DownloadSource GameFileMirrorSource { get; set; } = DownloadSource.BmclApi;

    /// <summary>
    /// Active resource-file CDN mirror (e.g. the temporary "Tianpao" source). When null,
    /// resource file URLs are never rewritten.
    /// </summary>
    public static IResourceMirror? ResourceMirror { get; set; }

    public static void Configure(
        SourceSelectionMode mode,
        TimeSpan? probeTimeout = null,
        TimeSpan? probeTtl = null,
        int? maxAttempts = null) {
        _mode = mode;
        if (probeTimeout is { } timeout)
            _probeTimeout = timeout;
        if (probeTtl is { } ttl)
            _probeTtl = ttl;
        if (maxAttempts is { } attempts)
            _maxAttempts = Math.Max(1, attempts);
    }

    /// <summary>
    /// Orders the primary (official) and mirror candidate URLs for download according to the
    /// current mode. Returns a single-element list when there is no mirror.
    /// </summary>
    public static async Task<IReadOnlyList<string>> OrderUrlsAsync(
        string primary,
        string? mirror,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(mirror) ||
            string.Equals(primary, mirror, StringComparison.OrdinalIgnoreCase))
            return [primary];

        return _mode switch {
            SourceSelectionMode.OfficialOnly => [primary],
            SourceSelectionMode.OfficialPreferred => [primary, mirror],
            SourceSelectionMode.MirrorPreferred => [mirror, primary],
            _ => await OrderByLatencyAsync(primary, mirror, cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<string>> OrderByLatencyAsync(
        string primary,
        string mirror,
        CancellationToken cancellationToken) {
        var primaryProbe = await ProbeAsync(primary, cancellationToken);
        var mirrorProbe = await ProbeAsync(mirror, cancellationToken);

        // A reachable candidate always wins over an unreachable one.
        if (primaryProbe.LatencyMs is null && mirrorProbe.LatencyMs is null)
            return [primary, mirror];
        if (primaryProbe.LatencyMs is null)
            return [mirror, primary];
        if (mirrorProbe.LatencyMs is null)
            return [primary, mirror];

        return primaryProbe.LatencyMs <= mirrorProbe.LatencyMs
            ? [primary, mirror]
            : [mirror, primary];
    }

    private static async Task<CachedProbe> ProbeAsync(string url, CancellationToken cancellationToken) {
        var host = GetHost(url);
        if (ProbeCache.TryGetValue(host, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached;

        // Single-flight per host so a burst of downloads never hammers the mirrors.
        await ProbeLock.WaitAsync(cancellationToken);
        try {
            if (ProbeCache.TryGetValue(host, out cached) && cached.ExpiresAt > DateTime.UtcNow)
                return cached;

            cached = await ProbeHostAsync(url, cancellationToken);
            ProbeCache[host] = cached;
            return cached;
        } finally {
            ProbeLock.Release();
        }
    }

    private static async Task<CachedProbe> ProbeHostAsync(string url, CancellationToken cancellationToken) {
        long? latency = null;
        try {
            var stopwatch = Stopwatch.StartNew();
            using var response = await url
                .AllowAnyHttpStatus()
                .WithTimeout(_probeTimeout)
                .HeadAsync(HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();

            if (response.ResponseMessage.IsSuccessStatusCode)
                latency = stopwatch.ElapsedMilliseconds;
        } catch {
            // Unreachable host; the download fallback chain will handle it.
        }

        return new CachedProbe(latency, DateTime.UtcNow + _probeTtl);
    }

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private readonly record struct CachedProbe(long? LatencyMs, DateTime ExpiresAt);
}

using System.Text.Json;
using Flurl.Http;
using Iridium.Installation.Models;

namespace Iridium.Installation;

/// <summary>
/// Fetches the Mojang version manifest listing downloadable Minecraft versions.
/// </summary>
public static class VersionManifestSource {
    private const string VersionManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    public static async Task<IReadOnlyList<VersionManifestEntry>?> GetVersionsAsync(CancellationToken ct = default) {
        await using var stream = await VersionManifestUrl
            .GetStreamAsync(HttpCompletionOption.ResponseContentRead, ct);

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        return document.RootElement
            .GetProperty("versions")
            .Deserialize<IEnumerable<VersionManifestEntry>>(
                VersionManifestEntryContext.Default.IEnumerableVersionManifestEntry)?
            .ToList();
    }
}

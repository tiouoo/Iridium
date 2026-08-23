using System.Text.Json;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers.PortalMc;

/// <summary>
/// Portal MC stores every instance as a fabric-style inherited version manifest under
/// <c>instances/&lt;name&gt;/&lt;name&gt;.json</c>. The manifest declares an
/// <c>inheritsFrom</c> id pointing at a vanilla version whose full metadata lives in
/// <c>meta/versions/&lt;id&gt;/&lt;id&gt;.json</c>; this provider walks that chain and
/// merges libraries, main class and arguments just like the standard provider does.
/// </summary>
sealed class PortalMcProvider(DirectoryInfo root) : IMinecraftProvider {
    private readonly string _versionsRoot = Path.Combine(root.FullName, "meta", "versions");

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        if (!Directory.Exists(instancesRoot))
            return [];

        var entries = new List<MinecraftEntry>();
        foreach (var dir in Directory.EnumerateDirectories(instancesRoot)) {
            var entry = await ParseAsync(dir, cancellationToken);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    public async Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken cancellationToken = default) {
        var dir = Path.Combine(root.FullName, "instances", id);
        if (!Directory.Exists(dir))
            return null;

        return await ParseAsync(dir, cancellationToken);
    }

    private async Task<MinecraftEntry?> ParseAsync(string instanceDir, CancellationToken cancellationToken) {
        var jsonPath = Path.Combine(instanceDir, $"{Path.GetFileName(instanceDir)}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var manifest = document.RootElement;

        var inheritsFrom = manifest.TryGetProperty("inheritsFrom", out var inheritsElement)
            ? inheritsElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(inheritsFrom))
            return null;

        // Resolve the inherited chain first so vanilla libraries come first, then
        // overlay the instance manifest itself (loader libraries, main class, ...).
        var merged = await MergeInheritanceAsync(inheritsFrom, new MergedVersion(), cancellationToken);
        if (merged is null)
            return null;

        ApplyLayer(merged, manifest, fallbackId: inheritsFrom);

        var id = Path.GetFileName(instanceDir);

        return new MinecraftEntry {
            Id = id,
            Name = id,
            MinecraftVersion = merged.MinecraftVersion,
            InstancePath = instanceDir,
            InheritsFrom = inheritsFrom,
            Format = PortalMcConstants.Format,
            Layout = new PortalMcLayout(),
            MainClass = merged.MainClass,
            Arguments = merged.Arguments,
            MinecraftArguments = merged.MinecraftArguments,
            Libraries = merged.Libraries,
            RequiredJavaVersion = merged.RequiredJavaVersion,
            AssetIndex = merged.AssetIndex,
            AssetIndexUrl = merged.AssetIndexUrl,
            ClientDownload = merged.ClientDownload,
            Jar = merged.Jar,
            Type = merged.Type,
            ReleaseTime = merged.ReleaseTime,
            Loaders = merged.Loaders
        };
    }

    private async Task<MergedVersion?> MergeInheritanceAsync(string versionId, MergedVersion merged, CancellationToken cancellationToken, int depth = 0) {
        if (depth > 8)
            return merged;

        var jsonPath = Path.Combine(_versionsRoot, versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            return merged;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Resolve the parent (vanilla) metadata first so child data overrides it.
        var parentId = root.TryGetProperty("inheritsFrom", out var parentElement)
            ? parentElement.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(parentId) && parentId != versionId)
            merged = await MergeInheritanceAsync(parentId, merged, cancellationToken, depth + 1) ?? merged;

        ApplyLayer(merged, root, versionId);

        return merged;
    }

    private static void ApplyLayer(MergedVersion merged, JsonElement root, string? fallbackId) {
        if (merged.MinecraftVersion is null)
            merged.MinecraftVersion = root.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } versionValue
                ? versionValue
                : fallbackId;

        if (root.TryGetProperty("mainClass", out var mainClass) && mainClass.GetString() is { Length: > 0 } mainClassValue)
            merged.MainClass = mainClassValue;

        if (root.TryGetProperty("minecraftArguments", out var mcArgs) && mcArgs.GetString() is { Length: > 0 } mcArgsValue)
            merged.MinecraftArguments = mcArgsValue;

        if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object) {
            var game = VersionManifestParser.MapArguments(args, "game");
            var jvm = VersionManifestParser.MapArguments(args, "jvm");
            merged.Arguments = merged.Arguments with {
                Game = game.Count > 0
                    ? [.. merged.Arguments.Game, .. game]
                    : merged.Arguments.Game,
                Jvm = jvm.Count > 0
                    ? [.. merged.Arguments.Jvm, .. jvm]
                    : merged.Arguments.Jvm
            };
        }

        if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array) {
            foreach (var library in VersionManifestParser.MapLibraries(libraries))
                VersionManifestParser.AddLibrary(merged.Libraries, library);

            merged.Loaders = ModLoaderProbe.Probe(libraries, merged.Loaders);
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object) {
            if (assetIndex.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } assetId)
                merged.AssetIndex = new AssetIndex(assetId);
            if (assetIndex.TryGetProperty("url", out var urlElement))
                merged.AssetIndexUrl = urlElement.GetString();
        }

        if (root.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("client", out var client) && client.ValueKind == JsonValueKind.Object) {
            var url = client.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (!string.IsNullOrEmpty(url))
                merged.ClientDownload = new MinecraftFileDownload {
                    Url = url,
                    Size = client.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0L,
                    Sha1 = client.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null
                };
        }

        if (VersionManifestParser.MapJavaVersion(root) is { } requiredJava)
            merged.RequiredJavaVersion = requiredJava;

        merged.Type = VersionManifestParser.MapType(root);
        merged.ReleaseTime = VersionManifestParser.MapReleaseTime(root);
    }

    private sealed record MergedVersion {
        public string? MinecraftVersion { get; set; }
        public string? MainClass { get; set; }
        public string? MinecraftArguments { get; set; }
        public MinecraftArguments Arguments { get; set; } = new();
        public List<MinecraftLibrary> Libraries { get; } = [];
        public int? RequiredJavaVersion { get; set; }
        public AssetIndex? AssetIndex { get; set; }
        public string? AssetIndexUrl { get; set; }
        public MinecraftFileDownload? ClientDownload { get; set; }
        public string? Jar { get; set; }
        public Iridium.Enums.MinecraftVersionType Type { get; set; }
        public DateTime? ReleaseTime { get; set; }
        public IReadOnlyList<MinecraftLoader> Loaders { get; set; } = [];
    }
}

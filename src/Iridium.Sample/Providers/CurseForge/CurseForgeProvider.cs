using System.Text.Json;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers.CurseForge;

/// <summary>
/// Scans CurseForge launcher installations. Each instance under
/// <c>Instances/&lt;name&gt;</c> carries a <c>minecraftinstance.json</c> declaring the
/// game version and base mod loader (e.g. <c>forge-47.4.1</c>); the loader's version
/// manifest lives in <c>Install/versions</c> and may inherit from the vanilla manifest.
/// </summary>
sealed class CurseForgeProvider(DirectoryInfo root) : IMinecraftProvider {
    private readonly string _installRoot = Path.Combine(root.FullName, "Install");

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var instancesRoot = Path.Combine(root.FullName, "Instances");
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
        var dir = Path.Combine(root.FullName, "Instances", id);
        if (!Directory.Exists(dir))
            return null;

        return await ParseAsync(dir, cancellationToken);
    }

    private async Task<MinecraftEntry?> ParseAsync(string instanceDir, CancellationToken cancellationToken) {
        var metadataPath = Path.Combine(instanceDir, "minecraftinstance.json");
        if (!File.Exists(metadataPath))
            return null;

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement;

        if (meta.TryGetProperty("isValid", out var valid) && !valid.GetBoolean())
            return null;
        if (meta.TryGetProperty("isEnabled", out var enabled) && !enabled.GetBoolean())
            return null;

        var gameVersion = meta.TryGetProperty("gameVersion", out var gameVersionElement)
            ? gameVersionElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(gameVersion))
            return null;

        var versionId = gameVersion;
        var loader = "vanilla";
        string? loaderVersion = null;
        if (meta.TryGetProperty("baseModLoader", out var baseLoader) && baseLoader.ValueKind == JsonValueKind.Object) {
            if (baseLoader.TryGetProperty("name", out var loaderNameElement) && loaderNameElement.GetString() is { Length: > 0 } loaderName)
                versionId = loaderName;
            loaderVersion = baseLoader.TryGetProperty("forgeVersion", out var forgeVersionElement)
                ? forgeVersionElement.GetString()
                : null;
            loader = versionId;
        }

        var merged = await MergeVersionAsync(versionId, cancellationToken);
        if (merged is null)
            return null;

        var id = Path.GetFileName(instanceDir);
        var name = meta.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? id
            : id;

        return new MinecraftEntry {
            Id = id,
            Name = name,
            MinecraftVersion = merged.MinecraftVersion ?? versionId,
            InstancePath = instanceDir,
            InheritsFrom = merged.InheritsFrom,
            Format = CurseForgeConstants.Format,
            Layout = new CurseForgeLayout(),
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
            Loaders = merged.Loaders.Count > 0
                ? merged.Loaders
                : ParseLoader(loader, loaderVersion)
        };
    }

    private async Task<MergedVersion?> MergeVersionAsync(string versionId, CancellationToken cancellationToken, int depth = 0) {
        if (depth > 8)
            return null;

        var jsonPath = Path.Combine(_installRoot, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var parentId = root.TryGetProperty("inheritsFrom", out var parentElement)
            ? parentElement.GetString()
            : null;

        MergedVersion merged = new();
        if (!string.IsNullOrWhiteSpace(parentId) && parentId != versionId) {
            var parent = await MergeVersionAsync(parentId, cancellationToken, depth + 1);
            if (parent is not null) {
                merged = parent;
                merged.InheritsFrom = parentId;
            }
        }

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

    private static IReadOnlyList<MinecraftLoader> ParseLoader(string loader, string? loaderVersion) {
        if (loader.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = Iridium.Enums.LoaderType.NeoForge, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = Iridium.Enums.LoaderType.Forge, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = Iridium.Enums.LoaderType.Fabric, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("quilt", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = Iridium.Enums.LoaderType.Quilt, Version = loaderVersion ?? string.Empty }];

        return [];
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
        public string? InheritsFrom { get; set; }
        public Iridium.Enums.MinecraftVersionType Type { get; set; }
        public DateTime? ReleaseTime { get; set; }
        public IReadOnlyList<MinecraftLoader> Loaders { get; set; } = [];
    }
}

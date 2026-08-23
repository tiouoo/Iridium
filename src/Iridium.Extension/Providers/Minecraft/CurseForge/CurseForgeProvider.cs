using System.Text.Json;
using Iridium.Enums;
using Iridium.Extension.Parsers.Launch;
using Iridium.Extension.Parsers.Minecraft;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Extension.Providers.Minecraft.CurseForge;

/// <summary>
/// Scans CurseForge launcher installations. Each instance under
/// <c>Instances/&lt;name&gt;</c> carries a <c>minecraftinstance.json</c> declaring the game
/// version and base mod loader (e.g. <c>forge-47.4.1</c>); the loader's version manifest
/// lives in <c>Install/versions</c> and may inherit from the vanilla manifest.
/// </summary>
public sealed class CurseForgeProvider : IMinecraftProvider {
    private readonly string _root;
    private readonly string _installRoot;
    private readonly VersionManifestMerger _merger;

    public CurseForgeProvider(DirectoryInfo root) {
        ArgumentNullException.ThrowIfNull(root);
        _root = root.FullName;
        _installRoot = Path.Combine(_root, "Install");
        _merger = new VersionManifestMerger(id => Path.Combine(_installRoot, "versions", id, $"{id}.json"));
    }

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var instancesRoot = Path.Combine(_root, "Instances");
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
        var dir = Path.Combine(_root, "Instances", id);
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

        var merged = await _merger.MergeAsync(versionId, cancellationToken);
        if (merged is null)
            return null;

        var id = Path.GetFileName(instanceDir);
        var name = meta.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? id
            : id;

        return new MinecraftEntry {
            Id = id,
            Name = name,
            MinecraftVersion = merged.MinecraftVersion.Length > 0 ? merged.MinecraftVersion : versionId,
            InstancePath = instanceDir,
            InheritsFrom = merged.InheritsFrom,
            Format = MinecraftFormat.Create("CurseForge"),
            Layout = new CurseForgeLayout(),
            MainClass = merged.MainClass,
            Arguments = merged.Arguments,
            MinecraftArguments = merged.MinecraftArguments,
            Libraries = merged.Libraries,
            RequiredJavaVersion = merged.RequiredJavaVersion,
            AssetIndex = merged.AssetIndex,
            AssetIndexUrl = merged.AssetIndexUrl,
            ClientDownload = merged.ClientDownload,
            Type = merged.Type,
            ReleaseTime = merged.ReleaseTime,
            Loaders = merged.Loaders.Count > 0
                ? merged.Loaders
                : ParseLoader(loader, loaderVersion)
        };
    }

    private static IReadOnlyList<MinecraftLoader> ParseLoader(string loader, string? loaderVersion) {
        if (loader.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = LoaderType.NeoForge, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = LoaderType.Forge, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("fabric", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = LoaderType.Fabric, Version = loaderVersion ?? string.Empty }];
        if (loader.Contains("quilt", StringComparison.OrdinalIgnoreCase))
            return [new MinecraftLoader { Type = LoaderType.Quilt, Version = loaderVersion ?? string.Empty }];

        return [];
    }
}

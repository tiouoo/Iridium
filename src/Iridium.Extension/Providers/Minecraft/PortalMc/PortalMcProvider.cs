using System.Text.Json;
using Iridium.Enums;
using Iridium.Extension.Parsers.Launch;
using Iridium.Extension.Parsers.Minecraft;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Extension.Providers.Minecraft.PortalMc;

/// <summary>
/// Scans Portal MC installations. Every instance is a fabric-style inherited version manifest
/// stored under <c>instances/&lt;name&gt;/&lt;name&gt;.json</c>; it declares an
/// <c>inheritsFrom</c> id pointing at a vanilla version whose full metadata lives in
/// <c>meta/versions/&lt;id&gt;/&lt;id&gt;.json</c>. This provider walks that chain, merges
/// libraries / main class / arguments just like the standard provider does, then overlays the
/// instance manifest itself.
/// </summary>
public sealed class PortalMcProvider : IMinecraftProvider {
    private readonly string _root;
    private readonly string _versionsRoot;
    private readonly VersionManifestMerger _merger;

    public PortalMcProvider(DirectoryInfo root) {
        ArgumentNullException.ThrowIfNull(root);
        _root = root.FullName;
        _versionsRoot = Path.Combine(_root, "meta", "versions");
        _merger = new VersionManifestMerger(id => Path.Combine(_versionsRoot, id, $"{id}.json"));
    }

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var instancesRoot = Path.Combine(_root, "instances");
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
        var dir = Path.Combine(_root, "instances", id);
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

        // Resolve the inherited chain first so vanilla libraries come first, then overlay
        // the instance manifest itself (loader libraries, main class, ...).
        var merged = await _merger.MergeAsync(inheritsFrom, cancellationToken);
        if (merged is null)
            return null;

        VersionManifestMerger.ApplyLayer(merged, manifest, fallbackId: inheritsFrom);

        var id = Path.GetFileName(instanceDir);

        return new MinecraftEntry {
            Id = id,
            Name = id,
            MinecraftVersion = merged.MinecraftVersion,
            InstancePath = instanceDir,
            InheritsFrom = inheritsFrom,
            Format = MinecraftFormat.Create("PortalMc"),
            Layout = new PortalMcLayout(),
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
            Loaders = merged.Loaders
        };
    }
}

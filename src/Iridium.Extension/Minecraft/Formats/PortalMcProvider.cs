using System.Text.Json;
using IFormatProvider = Iridium.Minecraft.Formats.IFormatProvider;
using Iridium.Extension.Minecraft.Layout;
using Iridium.Extension.Minecraft.Parsing;
using Iridium.Minecraft;
using Iridium.Installation;
using Iridium.Minecraft.Models;

namespace Iridium.Extension.Minecraft.Formats;

/// <summary>
/// Scans Portal MC installations. Every instance is a fabric-style inherited version manifest
/// stored under <c>instances/&lt;name&gt;/&lt;name&gt;.json</c>; it declares an
/// <c>inheritsFrom</c> id pointing at a vanilla version whose full metadata lives in
/// <c>meta/versions/&lt;id&gt;/&lt;id&gt;.json</c>. This provider walks that chain, merges
/// libraries / main class / arguments just like the standard provider does, then overlays the
/// instance manifest itself.
/// </summary>
public sealed class PortalMcProvider : IFormatProvider {
    public string Id => "PortalMc";

    public int Priority => 80;

    public bool CanResolve(DirectoryInfo root) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        if (!Directory.Exists(instancesRoot))
            return false;

        // Distinguish from Prism's instances/: Portal MC instance dirs hold a manifest
        // named after the instance declaring an inheritsFrom chain.
        foreach (var dir in Directory.EnumerateDirectories(instancesRoot)) {
            var jsonPath = Path.Combine(dir, $"{Path.GetFileName(dir)}.json");
            if (!File.Exists(jsonPath))
                continue;

            try {
                using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (document.RootElement.TryGetProperty("inheritsFrom", out var inherits) &&
                    inherits.GetString() is { Length: > 0 })
                    return true;
            } catch (JsonException) {
            }
        }

        return false;
    }

    public async ValueTask<MinecraftContext?> TryResolveAsync(DirectoryInfo root, CancellationToken ct = default) {
        var jsonPath = Path.Combine(root.FullName, $"{root.Name}.json");
        if (!File.Exists(jsonPath))
            return null;

        var launcherRoot = Path.GetDirectoryName(Path.GetDirectoryName(root.FullName)) ?? root.FullName;
        var entry = await ParseAsync(root.FullName, launcherRoot, ct);
        return entry is null ? null : Wrap(root, entry);
    }

    public async ValueTask<IReadOnlyList<MinecraftContext>> GetMinecraftsAsync(DirectoryInfo root, CancellationToken ct = default) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        if (!Directory.Exists(instancesRoot))
            return [];

        var contexts = new List<MinecraftContext>();
        foreach (var dir in Directory.EnumerateDirectories(instancesRoot)) {
            var entry = await ParseAsync(dir, root.FullName, ct);
            if (entry is not null)
                contexts.Add(Wrap(new DirectoryInfo(dir), entry));
        }

        return contexts;
    }

    public void ConfigureInstallation(InstallTaskBuilder builder, MinecraftContext context) {
    }

    public void ConfigureArguments(Iridium.Minecraft.Arguments.ArgumentBuilder builder, MinecraftContext context) {
    }

    private static MinecraftContext Wrap(DirectoryInfo dir, MinecraftEntry entry) => new() {
        Format = "PortalMc",
        Layout = new PortalMcLayout(),
        Entry = entry,
        Provider = new PortalMcProvider()
    };

    private async Task<MinecraftEntry?> ParseAsync(string instanceDir, string launcherRoot, CancellationToken ct) {
        var jsonPath = Path.Combine(instanceDir, $"{Path.GetFileName(instanceDir)}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        using var document = JsonDocument.Parse(json);
        var manifest = document.RootElement;

        var inheritsFrom = manifest.TryGetProperty("inheritsFrom", out var inheritsElement)
            ? inheritsElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(inheritsFrom))
            return null;

        // Resolve the inherited chain first so vanilla libraries come first, then overlay
        // the instance manifest itself (loader libraries, main class, ...).
        var versionsRoot = Path.Combine(launcherRoot, "meta", "versions");
        var merger = new VersionManifestMerger(id => Path.Combine(versionsRoot, id, $"{id}.json"));
        var merged = await merger.MergeAsync(inheritsFrom, ct);
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

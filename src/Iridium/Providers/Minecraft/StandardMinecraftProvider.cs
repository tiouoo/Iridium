using System.Text.Json;
using Iridium.Enums;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;
using Iridium.Parsers.Minecraft;

namespace Iridium.Providers.Minecraft;

/// <summary>
/// Scans the traditional launcher layout where every version lives under a
/// <c>versions/</c> directory ({root}/versions/{id}/{id}.json).
/// </summary>
public sealed class StandardMinecraftProvider : IMinecraftProvider {
    private readonly DirectoryInfo _root;

    public StandardMinecraftProvider(DirectoryInfo root) {
        _root = root;
    }

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var versionsDir = new DirectoryInfo(Path.Combine(_root.FullName, "versions"));
        if (!versionsDir.Exists)
            return [];

        var entries = new List<MinecraftEntry>();
        foreach (var dir in versionsDir.EnumerateDirectories()) {
            if (File.Exists(Path.Combine(dir.FullName, ".pclignore")))
                continue;

            var entry = await ParseAsync(dir, cancellationToken);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    public async Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var dir = new DirectoryInfo(Path.Combine(_root.FullName, "versions", id));
        if (!dir.Exists || File.Exists(Path.Combine(dir.FullName, ".pclignore")))
            return null;

        return await ParseAsync(dir, cancellationToken);
    }

    private async Task<MinecraftEntry?> ParseAsync(DirectoryInfo dir, CancellationToken cancellationToken) {
        var jsonPath = Path.Combine(dir.FullName, $"{dir.Name}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var entry = VersionJsonParser.MapEntry(root, dir.Name);
        return entry with {
            MinecraftVersion = await ResolveVersionAsync(entry.Id, root, cancellationToken),
            RequiredJavaVersion = await ResolveJavaVersionAsync(root, cancellationToken),
            InstancePath = dir.FullName,
            Format = MinecraftFormat.Standard,
            Loaders = ModLoaderDetector.DetectFromLibraries(
                root.TryGetProperty("libraries", out var librariesElement) ? librariesElement : default)
        };
    }

    private async Task<string> ResolveVersionAsync(string fallbackId, JsonElement root, CancellationToken cancellationToken, int depth = 0) {
        if (depth > 8)
            return fallbackId;

        // Modded versions (Forge/Fabric/OptiFine...) inherit from the vanilla version JSON.
        if (root.TryGetProperty("inheritsFrom", out var inherits) && inherits.GetString() is { Length: > 0 } parentId) {
            var parentJsonPath = Path.Combine(_root.FullName, "versions", parentId, $"{parentId}.json");
            
            if (File.Exists(parentJsonPath)) {
                var parentJson = await File.ReadAllTextAsync(parentJsonPath, cancellationToken);
                using var parentDocument = JsonDocument.Parse(parentJson);
                return await ResolveVersionAsync(parentId, parentDocument.RootElement, cancellationToken, depth + 1);
            }
        }

        // PCL writes the real Minecraft version into clientVersion.
        if (root.TryGetProperty("clientVersion", out var clientVersion) &&
            clientVersion.GetString() is { Length: > 0 } pclVersion)
            return pclVersion;

        // HMCL stores the real Minecraft version in the "game" patch.
        if (root.TryGetProperty("patches", out var patches) && patches.ValueKind == JsonValueKind.Array) {
            var patchesEnumerable = patches.EnumerateArray().Where(patch => patch.ValueKind == JsonValueKind.Object);
            
            foreach (var patch in patchesEnumerable) {
                if (!patch.TryGetProperty("id", out var patchId) || patchId.GetString() != "game")
                    continue;

                if (patch.TryGetProperty("version", out var patchVersion) && patchVersion.GetString() is { Length: > 0 } hmclVersion)
                    return hmclVersion;
            }
        }

        return fallbackId;
    }

    private async Task<int?> ResolveJavaVersionAsync(JsonElement root, CancellationToken cancellationToken, int depth = 0) {
        if (depth > 8)
            return null;

        // The declared javaVersion is authoritative; loaders (Forge etc.) often inherit it
        // from the vanilla version they extend, so keep walking the chain while it is absent.
        if (VersionJsonParser.MapJavaVersion(root) is { } required)
            return required;

        if (root.TryGetProperty("inheritsFrom", out var inherits) && inherits.GetString() is { Length: > 0 } parentId) {
            var parentJsonPath = Path.Combine(_root.FullName, "versions", parentId, $"{parentId}.json");

            if (File.Exists(parentJsonPath)) {
                var parentJson = await File.ReadAllTextAsync(parentJsonPath, cancellationToken);
                using var parentDocument = JsonDocument.Parse(parentJson);
                return await ResolveJavaVersionAsync(parentDocument.RootElement, cancellationToken, depth + 1);
            }
        }

        return null;
    }
}

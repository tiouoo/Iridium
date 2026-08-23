using System.Text.Json;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;
using Iridium.Providers.Minecraft;

namespace Iridium.Sample;

public static class MinecraftScanner {
    private static readonly (string Name, string Path, Func<DirectoryInfo, IMinecraftProvider> Provider)[] Folders = [
        ("Portal MC", @"C:\Users\84067\AppData\Roaming\cc.tiouo.portal.minecraft", root => new PortalMcProvider(root)),
        ("Modrinth", @"C:\Users\84067\AppData\Roaming\ModrinthApp", root => new ModrinthProvider(root)),
        ("CurseForge", @"C:\Users\84067\curseforge\minecraft", root => new CurseForgeProvider(root)),
        ("Axolotl", @"C:\Users\84067\AppData\Roaming\red.ghs.axolotl", root => new ModrinthProvider(root)),
        ("BakaXL", @"C:\Users\84067\AppData\Roaming\.BakaXL\minecraft", root => new PrismMinecraftProvider(root)),
        ("MultiMC", @"D:\Temp\MultiMC", root => new PrismMinecraftProvider(root)),
        (".minecraft", @"D:\Minecraft\.minecraft", root => new StandardMinecraftProvider(root))
    ];

    public static async Task RunAsync() {
        Console.WriteLine();
        foreach (var (name, path, create) in Folders) {
            try {
                var provider = create(new DirectoryInfo(path));
                var instances = await provider.GetMinecraftsAsync();
                Console.WriteLine($"{name,-16} {instances.Count} instance(s)  ({provider.GetType().Name})");
            }
            catch (Exception exception) {
                Console.WriteLine($"{name,-16} ERROR - {exception.Message}");
            }
        }
    }
}

sealed class PortalMcProvider(DirectoryInfo root) : IMinecraftProvider {
    public Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken ct = default) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        var metaRoot = Path.Combine(root.FullName, "meta", "versions");
        var entries = new List<MinecraftEntry>();

        foreach (var dir in Directory.GetDirectories(instancesRoot)) {
            var id = Path.GetFileName(dir);
            var json = Path.Combine(dir, $"{id}.json");
            if (!File.Exists(json)) continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var inherits = doc.RootElement.TryGetProperty("inheritsFrom", out var node) ? node.GetString() : null;
            if (string.IsNullOrWhiteSpace(inherits)) continue;

            entries.Add(new MinecraftEntry {
                Id = id,
                Name = id,
                MinecraftVersion = inherits,
                InstancePath = dir,
                InheritsFrom = inherits,
                RequiredJavaVersion = ReadRequiredJava(Path.Combine(metaRoot, inherits, $"{inherits}.json"))
            });
        }

        return Task.FromResult<IReadOnlyList<MinecraftEntry>>(entries);
    }

    public Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken ct = default) {
        MinecraftEntry? entry = null;
        foreach (var e in GetMinecraftsAsync(ct).Result)
            if (e.Id == id) entry = e;
        return Task.FromResult(entry);
    }

    private static int? ReadRequiredJava(string vanillaJsonPath) {
        if (!File.Exists(vanillaJsonPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(vanillaJsonPath));
        return doc.RootElement.TryGetProperty("javaVersion", out var jv) &&
               jv.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var v)
            ? v
            : null;
    }
}

sealed class ModrinthProvider(DirectoryInfo root) : IMinecraftProvider {
    public Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken ct = default) {
        var profilesRoot = Path.Combine(root.FullName, "profiles");
        var metaRoot = Path.Combine(root.FullName, "meta");
        var entries = new List<MinecraftEntry>();

        if (!Directory.Exists(profilesRoot))
            return Task.FromResult<IReadOnlyList<MinecraftEntry>>(entries);

        foreach (var dir in Directory.GetDirectories(profilesRoot)) {
            var version = Path.GetFileName(dir);
            var id = version;
            entries.Add(new MinecraftEntry {
                Id = id,
                Name = version,
                MinecraftVersion = version,
                InstancePath = dir,
                RequiredJavaVersion = ReadRequiredJava(Path.Combine(metaRoot, "versions", version, $"{version}.json"))
            });
        }

        return Task.FromResult<IReadOnlyList<MinecraftEntry>>(entries);
    }

    public Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken ct = default) {
        MinecraftEntry? entry = null;
        foreach (var e in GetMinecraftsAsync(ct).Result)
            if (e.Id == id) entry = e;
        return Task.FromResult(entry);
    }

    private static int? ReadRequiredJava(string versionJsonPath) {
        if (!File.Exists(versionJsonPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(versionJsonPath));
        return doc.RootElement.TryGetProperty("javaVersion", out var jv) &&
               jv.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var v)
            ? v
            : null;
    }
}

sealed class CurseForgeProvider(DirectoryInfo root) : IMinecraftProvider {
    public Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken ct = default) {
        var instancesRoot = Path.Combine(root.FullName, "Instances");
        var installRoot = Path.Combine(root.FullName, "Install");
        var entries = new List<MinecraftEntry>();

        foreach (var dir in Directory.GetDirectories(instancesRoot)) {
            var meta = Path.Combine(dir, "minecraftinstance.json");
            if (!File.Exists(meta)) continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            var rootElem = doc.RootElement;
            var gameVersion = rootElem.TryGetProperty("gameVersion", out var gv) ? gv.GetString() : null;
            if (string.IsNullOrWhiteSpace(gameVersion)) continue;

            entries.Add(new MinecraftEntry {
                Id = Path.GetFileName(dir),
                Name = rootElem.TryGetProperty("name", out var name) ? name.GetString() ?? Path.GetFileName(dir) : Path.GetFileName(dir),
                MinecraftVersion = gameVersion,
                InstancePath = dir,
                RequiredJavaVersion = ReadRequiredJava(Path.Combine(installRoot, "versions", gameVersion, $"{gameVersion}.json"))
            });
        }

        return Task.FromResult<IReadOnlyList<MinecraftEntry>>(entries);
    }

    public Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken ct = default) {
        MinecraftEntry? entry = null;
        foreach (var e in GetMinecraftsAsync(ct).Result)
            if (e.Id == id) entry = e;
        return Task.FromResult(entry);
    }

    private static int? ReadRequiredJava(string versionJsonPath) {
        if (!File.Exists(versionJsonPath)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(versionJsonPath));
        return doc.RootElement.TryGetProperty("javaVersion", out var jv) &&
               jv.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var v)
            ? v
            : null;
    }
}

using Iridium.Enums;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers.Modrinth;

internal static class ModrinthConstants {
    public static readonly MinecraftFormat Format = MinecraftFormat.Create("Modrinth");
}

/// <summary>
/// Modrinth keeps its shared metadata under a <c>meta/</c> directory: version manifests
/// and jars in <c>meta/versions/&lt;id&gt;</c>, libraries in <c>meta/libraries</c>,
/// assets in <c>meta/assets</c> and per-profile native libraries in
/// <c>meta/natives/&lt;profile&gt;</c>. Each profile (game directory) lives directly under
/// <c>profiles/&lt;name&gt;</c>.
/// </summary>
sealed class ModrinthLayout : IMinecraftLayout {
    public MinecraftFormat Format => ModrinthConstants.Format;

    public string GetInstanceDirectory(string id) => Path.Combine("profiles", id);

    public string GetInstanceRoot(MinecraftEntry entry) => entry.InstancePath;

    public string GetGameDirectory(MinecraftEntry entry) => entry.InstancePath;

    public string GetLibrariesRoot(MinecraftEntry entry) => Path.Combine(GetMetaRoot(entry), "libraries");

    public string GetAssetsRoot(MinecraftEntry entry) => Path.Combine(GetMetaRoot(entry), "assets");

    public string GetNativesDirectory(MinecraftEntry entry) =>
        Path.Combine(GetMetaRoot(entry), "natives", Path.GetFileName(entry.InstancePath));

    public string GetVersionJarPath(MinecraftEntry entry) {
        var id = ResolveVersionId(entry);
        return Path.Combine(GetMetaRoot(entry), "versions", id, $"{id}.jar");
    }

    public string GetVersionJsonPath(MinecraftEntry entry) {
        var id = ResolveVersionId(entry);
        return Path.Combine(GetMetaRoot(entry), "versions", id, $"{id}.json");
    }

    private static string ResolveVersionId(MinecraftEntry entry) =>
        entry.VersionId.Length > 0 ? entry.VersionId
        : entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion
        : entry.Id;

    private static string GetMetaRoot(MinecraftEntry entry) {
        // InstancePath = {root}/profiles/{name} -> meta root is {root}/meta
        var profileDir = Path.GetFullPath(entry.InstancePath);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(profileDir)) ?? profileDir;
        return Path.Combine(root, "meta");
    }
}

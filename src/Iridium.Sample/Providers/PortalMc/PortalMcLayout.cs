using Iridium.Enums;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers.PortalMc;

internal static class PortalMcConstants {
    public static readonly MinecraftFormat Format = MinecraftFormat.Create("PortalMc");
}

/// <summary>
/// Portal MC keeps its shared metadata under a <c>meta/</c> directory instead of the
/// launcher root: libraries in <c>meta/libraries</c>, assets in <c>meta/assets</c>,
/// native libraries in <c>meta/natives</c> and version manifests/jars in
/// <c>meta/versions/&lt;id&gt;</c>. Instances live under <c>instances/&lt;name&gt;</c>
/// with game files directly inside the instance directory.
/// </summary>
sealed class PortalMcLayout : IMinecraftLayout {
    public MinecraftFormat Format => PortalMcConstants.Format;

    public string GetInstanceDirectory(string id) => Path.Combine("instances", id);

    public string GetInstanceRoot(MinecraftEntry entry) => entry.InstancePath;

    public string GetGameDirectory(MinecraftEntry entry) => entry.InstancePath;

    public string GetLibrariesRoot(MinecraftEntry entry) => Path.Combine(GetMetaRoot(entry), "libraries");

    public string GetAssetsRoot(MinecraftEntry entry) => Path.Combine(GetMetaRoot(entry), "assets");

    public string GetNativesDirectory(MinecraftEntry entry) => Path.Combine(GetMetaRoot(entry), "natives");

    public string GetVersionJarPath(MinecraftEntry entry) {
        var id = entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion : entry.Id;
        return Path.Combine(GetMetaRoot(entry), "versions", id, $"{id}.jar");
    }

    public string GetVersionJsonPath(MinecraftEntry entry) {
        var id = entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion : entry.Id;
        return Path.Combine(GetMetaRoot(entry), "versions", id, $"{id}.json");
    }

    private static string GetMetaRoot(MinecraftEntry entry) {
        // InstancePath = {root}/instances/{name} -> meta root is {root}/meta
        var instanceDir = Path.GetFullPath(entry.InstancePath);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(instanceDir)) ?? instanceDir;
        return Path.Combine(root, "meta");
    }
}

using Iridium.Enums;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers.CurseForge;

internal static class CurseForgeConstants {
    public static readonly MinecraftFormat Format = MinecraftFormat.Create("CurseForge");
}

/// <summary>
/// CurseForge keeps its shared metadata under an <c>Install/</c> directory: version
/// manifests/jars in <c>Install/versions/&lt;id&gt;</c>, libraries in
/// <c>Install/libraries</c> and assets in <c>Install/assets</c>. Each instance (game
/// directory) lives under <c>Instances/&lt;name&gt;</c> with its native libraries in a
/// local <c>natives</c> folder.
/// </summary>
sealed class CurseForgeLayout : IMinecraftLayout {
    public MinecraftFormat Format => CurseForgeConstants.Format;

    public string GetInstanceDirectory(string id) => Path.Combine("Instances", id);

    public string GetInstanceRoot(MinecraftEntry entry) => entry.InstancePath;

    public string GetGameDirectory(MinecraftEntry entry) => entry.InstancePath;

    public string GetLibrariesRoot(MinecraftEntry entry) => Path.Combine(GetInstallRoot(entry), "libraries");

    public string GetAssetsRoot(MinecraftEntry entry) => Path.Combine(GetInstallRoot(entry), "assets");

    public string GetNativesDirectory(MinecraftEntry entry) => Path.Combine(entry.InstancePath, "natives");

    public string GetVersionJarPath(MinecraftEntry entry) {
        var id = entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion : entry.Id;
        return Path.Combine(GetInstallRoot(entry), "versions", id, $"{id}.jar");
    }

    public string GetVersionJsonPath(MinecraftEntry entry) {
        var id = entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion : entry.Id;
        return Path.Combine(GetInstallRoot(entry), "versions", id, $"{id}.json");
    }

    private static string GetInstallRoot(MinecraftEntry entry) {
        // InstancePath = {root}/Instances/{name} -> Install root is {root}/Install
        var instanceDir = Path.GetFullPath(entry.InstancePath);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(instanceDir)) ?? instanceDir;
        return Path.Combine(root, "Install");
    }
}

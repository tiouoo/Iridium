using Iridium.Enums;
using Iridium.Models.Java;
using Iridium.Models.Minecraft;
using Iridium.Providers.Java;

namespace Iridium.Extensions;

public static class MinecraftJavaExtensions {
    public static async Task<JavaEntry?> SelectAppropriateJavaAsync(this MinecraftEntry entry,
        IReadOnlyList<JavaEntry> javas, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(javas);
        if (javas.Count == 0)
            return null;

        var requiredVersion = entry.RequiredJavaVersion ?? InferRequiredJavaVersion(entry.MinecraftVersion);
        var requiresExactVersion = entry.Loaders.Any(loader =>
            loader.Type is LoaderType.Forge or LoaderType.NeoForge);

        var ordered = OrderJavaCandidates(javas, requiredVersion, requiresExactVersion);

        foreach (var candidate in ordered) {
            if (await JavaRuntimeVerifier.IsUsableAsync(candidate.JavaPath, candidate.MajorVersion, cancellationToken))
                return candidate;
        }

        return null;
    }

    private static IReadOnlyList<JavaEntry> OrderJavaCandidates(
        IReadOnlyList<JavaEntry> javas, int requiredVersion, bool requiresExactVersion) {
        var preferred = javas.Where(java => java.Is64Bit).ToList();
        if (preferred.Count == 0)
            preferred = [.. javas];

        var compatible = preferred.Where(IsCompatible).OrderBy(java => java.MajorVersion).ToList();
        var incompatible = preferred.Where(java => !IsCompatible(java))
            .OrderByDescending(java => java.MajorVersion)
            .ToList();
        return [.. compatible, .. incompatible];

        bool IsCompatible(JavaEntry java) =>
            requiredVersion is 0 or -1 ||
            (requiresExactVersion ? java.MajorVersion == requiredVersion : java.MajorVersion >= requiredVersion);
    }

    /// <summary>
    /// Infers the minimum Java major version from the Minecraft release when the version
    /// JSON does not declare one (e.g. third-party meta files). 1.20.5+ and every modern
    /// release need Java 21, 1.18-1.20.4 need 17, 1.17 needs 16, everything older runs on 8.
    /// </summary>
    private static int InferRequiredJavaVersion(string minecraftVersion) {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return 8;

        var parts = minecraftVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts[0] == "1") {
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var parsedMinor) ? parsedMinor : 0;
            var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;

            if (minor >= 21 || (minor == 20 && patch >= 5))
                return 21;
            if (minor >= 18)
                return 17;
            if (minor == 17)
                return 16;
            return 8;
        }

        // New-style release numbering (26.2, ...) follows the same Java requirements as 1.21+.
        return int.TryParse(parts[0], out var major) && major >= 22 ? 21 : 8;
    }
}

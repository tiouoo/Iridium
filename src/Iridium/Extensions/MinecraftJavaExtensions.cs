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

        var requiredVersion = entry.RequiredJavaVersion ?? 8;
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
}

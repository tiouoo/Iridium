using System.Text.Json;
using Iridium.Enums;
using Iridium.Models.Minecraft;

namespace Iridium.Sample.Providers;

/// <summary>
/// Shared helpers used by the sample providers to parse Mojang-style version manifests
/// (arguments, libraries, rules and loader detection).
/// </summary>
internal static class VersionManifestParser {
    public static List<MinecraftArgument> MapArguments(JsonElement arguments, string key) {
        var result = new List<MinecraftArgument>();
        if (!arguments.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in list.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String) {
                if (item.GetString() is { Length: > 0 } value)
                    result.Add(new MinecraftArgument { Values = [value] });
            } else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var valueElement)) {
                var values = new List<string>();
                if (valueElement.ValueKind == JsonValueKind.Array) {
                    foreach (var element in valueElement.EnumerateArray())
                        if (element.GetString() is { Length: > 0 } value)
                            values.Add(value);
                } else if (valueElement.GetString() is { Length: > 0 } value)
                    values.Add(value);

                if (values.Count > 0)
                    result.Add(new MinecraftArgument { Values = values, Rules = MapRules(item) });
            }
        }

        return result;
    }

    public static IReadOnlyList<CompatibilityRule>? MapRules(JsonElement element) {
        if (!element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() == 0)
            return null;

        var result = new List<CompatibilityRule>();
        foreach (var rule in rules.EnumerateArray()) {
            if (rule.ValueKind != JsonValueKind.Object)
                continue;

            var action = rule.TryGetProperty("action", out var actionElement) && actionElement.GetString() == "disallow"
                ? CompatibilityRuleAction.Disallow
                : CompatibilityRuleAction.Allow;

            string? osName = null;
            string? osVersion = null;
            string? osArch = null;
            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object) {
                osName = os.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                osVersion = os.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
                osArch = os.TryGetProperty("arch", out var archElement) ? archElement.GetString() : null;
            }

            IReadOnlyDictionary<string, bool>? features = null;
            if (rule.TryGetProperty("features", out var featureElement) && featureElement.ValueKind == JsonValueKind.Object) {
                var featuresDict = new Dictionary<string, bool>();
                foreach (var feature in featureElement.EnumerateObject())
                    featuresDict[feature.Name] = feature.Value.ValueKind == JsonValueKind.True;
                features = featuresDict;
            }

            result.Add(new CompatibilityRule {
                Action = action,
                OsName = osName,
                OsVersion = osVersion,
                OsArch = osArch,
                Features = features
            });
        }

        return result.Count > 0 ? result : null;
    }

    public static IEnumerable<MinecraftLibrary> MapLibraries(JsonElement libraries) {
        foreach (var library in libraries.EnumerateArray()) {
            if (library.ValueKind != JsonValueKind.Object ||
                !library.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { Length: > 0 } name)
                continue;

            // Modrinth/Forge metas mark tooling jars (e.g. ForgeAutoRenamingTool) as
            // excluded from the classpath; including them causes JPMS split-package errors.
            if (library.TryGetProperty("include_in_classpath", out var include) &&
                include.ValueKind == JsonValueKind.False)
                continue;

            var (url, artifactPath) = MapArtifact(library);

            yield return new MinecraftLibrary {
                Name = name,
                Url = url ?? (library.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null),
                Path = artifactPath,
                Rules = MapRules(library),
                Natives = MapNatives(library)
            };
        }
    }

    private static (string? Url, string? Path) MapArtifact(JsonElement element) {
        if (element.TryGetProperty("downloads", out var downloads) &&
            downloads.ValueKind == JsonValueKind.Object &&
            downloads.TryGetProperty("artifact", out var artifact) &&
            artifact.ValueKind == JsonValueKind.Object) {
            var url = artifact.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var path = artifact.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
            return (url, path);
        }

        return (null, null);
    }

    /// <summary>
    /// Adds a library. Unconditional duplicates collapse to the higher version (mirrors
    /// Prism's applyLibrary); platform-rule constrained variants are all kept so the
    /// matching rules can be evaluated at launch time (e.g. jna 5.13.0 for mac + jna
    /// 5.12.1 for windows).
    /// </summary>
    public static void AddLibrary(List<MinecraftLibrary> libraries, MinecraftLibrary library) {
        var key = GetArtifactKey(library.Name);

        // Rule-constrained library: keep it alongside any unconditional or other-rule
        // variants; the launcher filters by platform rules when building the classpath.
        if (library.Rules is { Count: > 0 }) {
            if (!libraries.Any(existing => SameArtifact(existing, key) &&
                                           (existing.Rules?.Count ?? 0) == 0))
                libraries.Add(library);
            return;
        }

        for (var i = 0; i < libraries.Count; i++) {
            if (!string.Equals(GetArtifactKey(libraries[i].Name), key, StringComparison.Ordinal))
                continue;

            // Existing entry is platform-specific; keep the unconditional one too.
            if (libraries[i].Rules is { Count: > 0 })
                return;

            if (CompareVersions(GetLibraryVersion(library.Name), GetLibraryVersion(libraries[i].Name)) > 0)
                libraries[i] = library;

            return;
        }

        libraries.Add(library);
    }

    private static bool SameArtifact(MinecraftLibrary library, string key) =>
        string.Equals(GetArtifactKey(library.Name), key, StringComparison.Ordinal);

    private static string GetArtifactKey(string name) {
        var parts = name.Split(':');
        if (parts.Length < 2)
            return name;

        // group:artifact[:classifier] -- natives like lwjgl-...:3.3.1:natives-linux must not
        // be collapsed into the plain artifact.
        var key = $"{parts[0]}:{parts[1]}";
        if (parts.Length >= 4)
            key += $":{parts[3]}";

        return key;
    }

    private static string GetLibraryVersion(string name) {
        var parts = name.Split(':');
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    private static int CompareVersions(string a, string b) {
        var aParts = a.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var bParts = b.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var count = Math.Max(aParts.Length, bParts.Length);

        for (var i = 0; i < count; i++) {
            var x = i < aParts.Length ? aParts[i] : string.Empty;
            var y = i < bParts.Length ? bParts[i] : string.Empty;
            if (x == y)
                continue;

            if (int.TryParse(x, out var xi) && int.TryParse(y, out var yi)) {
                var numeric = xi.CompareTo(yi);
                if (numeric != 0)
                    return numeric;
            } else {
                var ordinal = string.CompareOrdinal(x, y);
                if (ordinal != 0)
                    return ordinal;
            }
        }

        return 0;
    }

    public static Dictionary<string, string>? MapNatives(JsonElement element) {
        if (!element.TryGetProperty("natives", out var natives) || natives.ValueKind != JsonValueKind.Object)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var native in natives.EnumerateObject())
            result[native.Name] = native.Value.GetString() ?? string.Empty;

        return result;
    }

    public static MinecraftFileDownload? MapClientDownload(JsonElement root) {
        if (!root.TryGetProperty("downloads", out var downloads) ||
            !downloads.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.Object)
            return null;

        var url = client.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
        if (string.IsNullOrEmpty(url))
            return null;

        return new MinecraftFileDownload {
            Url = url,
            Size = client.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0L,
            Sha1 = client.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null
        };
    }

    public static int? MapJavaVersion(JsonElement root) {
        if (!root.TryGetProperty("javaVersion", out var javaVersion) ||
            !javaVersion.TryGetProperty("majorVersion", out var major) ||
            !major.TryGetInt32(out var value))
            return null;

        return value;
    }

    public static MinecraftVersionType MapType(JsonElement root) {
        if (!root.TryGetProperty("type", out var type) || type.GetString() is not { } value)
            return MinecraftVersionType.Release;

        return value switch {
            "snapshot" => MinecraftVersionType.Snapshot,
            "old_beta" => MinecraftVersionType.OldBeta,
            "old_alpha" => MinecraftVersionType.OldAlpha,
            _ => MinecraftVersionType.Release
        };
    }

    public static DateTime? MapReleaseTime(JsonElement root) {
        if (!root.TryGetProperty("releaseTime", out var releaseTime) || releaseTime.GetString() is not { } value)
            return null;

        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }
}

/// <summary>
/// Minimal loader detection: matches loader artifacts by their Maven group/artifact,
/// mirroring MinecraftLaunch's loader table.
/// </summary>
internal static class ModLoaderProbe {
    private static readonly (string Group, string Artifact)[] Loaders = [
        ("net.fabricmc", "fabric-loader"),
        ("net.fabricmc", "fabric-api"),
        ("net.minecraftforge", "forge"),
        ("net.minecraftforge", "fmlloader"),
        ("net.minecraftforge", "fmlcore"),
        ("net.neoforged", "neoforge"),
        ("net.neoforged.fancymodloader", "loader"),
        ("org.quiltmc", "quilt-loader"),
        ("optifine", "optifine"),
        ("com.mumfrey", "liteloader")
    ];

    public static IReadOnlyList<MinecraftLoader> Probe(JsonElement libraries, IReadOnlyList<MinecraftLoader> existing) {
        var result = new List<MinecraftLoader>(existing);
        var seen = new HashSet<string>(existing.Select(l => $"{l.Type}:{l.Version}"));
        foreach (var library in libraries.EnumerateArray()) {
            if (!library.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { Length: > 0 } name)
                continue;

            var parts = name.Split(':');
            if (parts.Length < 2)
                continue;

            foreach (var (group, artifact) in Loaders) {
                if (parts[0] != group || parts[1] != artifact)
                    continue;

                var version = parts.Length >= 3 ? parts[2] : string.Empty;
                var type = artifact switch {
                    "fabric-loader" or "fabric-api" => LoaderType.Fabric,
                    "forge" or "fmlloader" or "fmlcore" => LoaderType.Forge,
                    "neoforge" or "loader" => LoaderType.NeoForge,
                    "quilt-loader" => LoaderType.Quilt,
                    "optifine" => LoaderType.Optifine,
                    "liteloader" => LoaderType.LiteLoader,
                    _ => LoaderType.Fabric
                };

                if (seen.Add($"{type}:{version}"))
                    result.Add(new MinecraftLoader { Type = type, Version = version });

                break;
            }
        }

        return result;
    }
}

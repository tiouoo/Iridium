using System.Text.Json;
using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;
using Microsoft.Data.Sqlite;

namespace Iridium.Sample.Providers.Modrinth;

/// <summary>
/// Scans Modrinth App installations. Instance metadata (profile path, game version and
/// loader) is stored in the launcher's <c>app.db</c> SQLite database; the resolved
/// version manifest lives in <c>meta/versions/&lt;id&gt;</c> as a fully merged (or
/// inherited) Mojang-style json.
/// </summary>
sealed class ModrinthProvider(DirectoryInfo root) : IMinecraftProvider {
    private readonly string _metadataRoot = Path.Combine(root.FullName, "meta");

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var databasePath = Path.Combine(root.FullName, "app.db");
        if (!File.Exists(databasePath))
            return [];

        var entries = new List<MinecraftEntry>();
        try {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT i.path, i.name, c.game_version, c.loader, c.loader_version
                FROM instances i
                LEFT JOIN instance_content_sets c ON c.id = i.applied_content_set_id
                WHERE i.install_stage = 'installed'
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken)) {
                var profilePath = reader.IsDBNull(0) ? null : reader.GetString(0);
                var displayName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var gameVersion = reader.IsDBNull(2) ? null : reader.GetString(2);
                var loader = reader.IsDBNull(3) ? null : reader.GetString(3);
                var loaderVersion = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (string.IsNullOrWhiteSpace(profilePath) || string.IsNullOrWhiteSpace(gameVersion))
                    continue;

                var profileDir = ResolveProfilePath(root.FullName, profilePath);
                if (!Directory.Exists(profileDir))
                    continue;

                var versionId = ResolveVersionId(gameVersion, loaderVersion);
                var entry = await ParseAsync(profileDir, displayName, gameVersion, versionId, loader, cancellationToken);
                if (entry is null && versionId != gameVersion) {
                    versionId = gameVersion;
                    entry = await ParseAsync(profileDir, displayName, gameVersion, versionId, loader, cancellationToken);
                }
                if (entry is not null)
                    entries.Add(entry);
            }
        } catch (SqliteException) {
            return [];
        } catch (IOException) {
            return [];
        }

        return entries;
    }

    public async Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken cancellationToken = default) {
        var instances = await GetMinecraftsAsync(cancellationToken);
        return instances.FirstOrDefault(e => e.Id == id);
    }

    private async Task<MinecraftEntry?> ParseAsync(
        string profileDir,
        string? displayName,
        string gameVersion,
        string versionId,
        string? loader,
        CancellationToken cancellationToken) {
        var merged = await MergeVersionAsync(versionId, cancellationToken);
        if (merged is null)
            return null;

        // The Modrinth metadata is fully merged, but some version manifests still carry
        // an inheritsFrom; loaders are detected from the manifest libraries themselves.
        var id = Path.GetFileName(profileDir);

        return new MinecraftEntry {
            Id = id,
            Name = displayName ?? id,
            MinecraftVersion = gameVersion,
            VersionId = versionId,
            InstancePath = profileDir,
            InheritsFrom = merged.InheritsFrom,
            Format = ModrinthConstants.Format,
            Layout = new ModrinthLayout(),
            MainClass = merged.MainClass,
            Arguments = merged.Arguments,
            MinecraftArguments = merged.MinecraftArguments,
            Libraries = merged.Libraries,
            RequiredJavaVersion = merged.RequiredJavaVersion,
            AssetIndex = merged.AssetIndex,
            AssetIndexUrl = merged.AssetIndexUrl,
            ClientDownload = merged.ClientDownload,
            Jar = merged.Jar,
            Type = merged.Type,
            ReleaseTime = merged.ReleaseTime,
            Loaders = merged.Loaders
        };
    }

    private async Task<MergedVersion?> MergeVersionAsync(string versionId, CancellationToken cancellationToken, int depth = 0) {
        if (depth > 8)
            return null;

        var jsonPath = Path.Combine(_metadataRoot, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var parentId = root.TryGetProperty("inheritsFrom", out var parentElement)
            ? parentElement.GetString()
            : null;

        MergedVersion merged = new();
        if (!string.IsNullOrWhiteSpace(parentId) && parentId != versionId) {
            var parent = await MergeVersionAsync(parentId, cancellationToken, depth + 1);
            if (parent is not null) {
                merged = parent;
                merged.InheritsFrom = parentId;
            }
        }

        ApplyLayer(merged, root, versionId);

        return merged;
    }

    private static void ApplyLayer(MergedVersion merged, JsonElement root, string? fallbackId) {
        if (merged.MinecraftVersion is null)
            merged.MinecraftVersion = root.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } versionValue
                ? versionValue
                : fallbackId;

        if (root.TryGetProperty("mainClass", out var mainClass) && mainClass.GetString() is { Length: > 0 } mainClassValue)
            merged.MainClass = mainClassValue;

        if (root.TryGetProperty("minecraftArguments", out var mcArgs) && mcArgs.GetString() is { Length: > 0 } mcArgsValue)
            merged.MinecraftArguments = mcArgsValue;

        if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object) {
            var game = VersionManifestParser.MapArguments(args, "game");
            var jvm = VersionManifestParser.MapArguments(args, "jvm");
            merged.Arguments = merged.Arguments with {
                Game = game.Count > 0
                    ? [.. merged.Arguments.Game, .. game]
                    : merged.Arguments.Game,
                Jvm = jvm.Count > 0
                    ? [.. merged.Arguments.Jvm, .. jvm]
                    : merged.Arguments.Jvm
            };
        }

        if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array) {
            foreach (var library in VersionManifestParser.MapLibraries(libraries))
                VersionManifestParser.AddLibrary(merged.Libraries, library);

            merged.Loaders = ModLoaderProbe.Probe(libraries, merged.Loaders);
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object) {
            if (assetIndex.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } assetId)
                merged.AssetIndex = new AssetIndex(assetId);
            if (assetIndex.TryGetProperty("url", out var urlElement))
                merged.AssetIndexUrl = urlElement.GetString();
        }

        if (root.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("client", out var client) && client.ValueKind == JsonValueKind.Object) {
            var url = client.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (!string.IsNullOrEmpty(url))
                merged.ClientDownload = new MinecraftFileDownload {
                    Url = url,
                    Size = client.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0L,
                    Sha1 = client.TryGetProperty("sha1", out var sha1Element) ? sha1Element.GetString() : null
                };
        }

        if (VersionManifestParser.MapJavaVersion(root) is { } requiredJava)
            merged.RequiredJavaVersion = requiredJava;

        merged.Type = VersionManifestParser.MapType(root);
        merged.ReleaseTime = VersionManifestParser.MapReleaseTime(root);
    }

    private static string ResolveProfilePath(string root, string profilePath) {
        if (Path.IsPathRooted(profilePath))
            return profilePath;

        var profilesRoot = Path.Combine(root, "profiles");
        var directPath = Path.Combine(root, profilePath);
        if (Path.GetFullPath(directPath).StartsWith(Path.GetFullPath(profilesRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return directPath;

        return Path.Combine(profilesRoot, profilePath);
    }

    private static string ResolveVersionId(string gameVersion, string? loaderVersion) {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return gameVersion;

        // Modrinth stores loader installs under <game>-<loaderVersion> (e.g. 1.20.1-47.4.10).
        // MergeVersionAsync falls back when the exact id has no manifest.
        return $"{gameVersion}-{loaderVersion}";
    }

    private sealed record MergedVersion {
        public string? MinecraftVersion { get; set; }
        public string? MainClass { get; set; }
        public string? MinecraftArguments { get; set; }
        public MinecraftArguments Arguments { get; set; } = new();
        public List<MinecraftLibrary> Libraries { get; } = [];
        public int? RequiredJavaVersion { get; set; }
        public AssetIndex? AssetIndex { get; set; }
        public string? AssetIndexUrl { get; set; }
        public MinecraftFileDownload? ClientDownload { get; set; }
        public string? Jar { get; set; }
        public string? InheritsFrom { get; set; }
        public Iridium.Enums.MinecraftVersionType Type { get; set; }
        public DateTime? ReleaseTime { get; set; }
        public IReadOnlyList<MinecraftLoader> Loaders { get; set; } = [];
    }
}

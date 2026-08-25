using Iridium.Enums;
using Iridium.Extension.Parsers.Launch;
using Iridium.Extension.Parsers.Minecraft;
using Iridium.Minecraft;
using Iridium.Minecraft.Models;
using Microsoft.Data.Sqlite;

namespace Iridium.Extension.Providers.Minecraft.Modrinth;

/// <summary>
/// Scans Modrinth App installations. Instance metadata (profile path, game version and
/// loader) is stored in the launcher's <c>app.db</c> SQLite database; the resolved version
/// manifest lives in <c>meta/versions/&lt;id&gt;</c> as a fully merged (or inherited)
/// Mojang-style json.
/// </summary>
public sealed class ModrinthProvider : IMinecraftProvider {
    private readonly string _root;
    private readonly string _metadataRoot;
    private readonly VersionManifestMerger _merger;

    public ModrinthProvider(DirectoryInfo root) {
        ArgumentNullException.ThrowIfNull(root);
        _root = root.FullName;
        _metadataRoot = Path.Combine(_root, "meta");
        _merger = new VersionManifestMerger(id => Path.Combine(_metadataRoot, "versions", id, $"{id}.json"));
    }

    public async Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default) {
        var databasePath = Path.Combine(_root, "app.db");
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
                var loaderVersion = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (string.IsNullOrWhiteSpace(profilePath) || string.IsNullOrWhiteSpace(gameVersion))
                    continue;

                var profileDir = ResolveProfilePath(profilePath);
                if (!Directory.Exists(profileDir))
                    continue;

                var versionId = ResolveVersionId(gameVersion, loaderVersion);
                var entry = await ParseAsync(profileDir, displayName, gameVersion, versionId, cancellationToken);
                if (entry is null && versionId != gameVersion)
                    entry = await ParseAsync(profileDir, displayName, gameVersion, gameVersion, cancellationToken);
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
        CancellationToken cancellationToken) {
        var merged = await _merger.MergeAsync(versionId, cancellationToken);
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
            Format = MinecraftFormat.Create("Modrinth"),
            Layout = new ModrinthLayout(),
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

    private string ResolveProfilePath(string profilePath) {
        if (Path.IsPathRooted(profilePath))
            return profilePath;

        var profilesRoot = Path.Combine(_root, "profiles");
        var directPath = Path.Combine(_root, profilePath);
        if (Path.GetFullPath(directPath).StartsWith(Path.GetFullPath(profilesRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return directPath;

        return Path.Combine(profilesRoot, profilePath);
    }

    private static string ResolveVersionId(string gameVersion, string? loaderVersion) {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return gameVersion;

        // Modrinth stores loader installs under <game>-<loaderVersion> (e.g. 1.20.1-47.4.10).
        return $"{gameVersion}-{loaderVersion}";
    }
}

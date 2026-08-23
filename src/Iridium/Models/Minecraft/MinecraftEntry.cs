using Iridium.Enums;
using Iridium.Interfaces.Minecraft;

namespace Iridium.Models.Minecraft;

public sealed record MinecraftEntry {
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string InstancePath { get; init; } = string.Empty;
    public string MinecraftVersion { get; init; } = string.Empty;
    public string VersionId { get; init; } = string.Empty;

    /// <summary>
    /// The Java major version the version manifest declares, resolved through the
    /// inheritance chain. Null when the manifest carries no explicit requirement.
    /// </summary>
    public int? RequiredJavaVersion { get; init; }

    public MinecraftFormat Format { get; init; }

    /// <summary>
    /// Optional custom directory mapping for this entry. When set, the launcher uses it
    /// instead of the default layout for the entry's <see cref="Format"/>, letting a
    /// provider describe exotic folder layouts (e.g. a shared <c>meta/</c> store) without
    /// registering a factory.
    /// </summary>
    public IMinecraftLayout? Layout { get; init; }

    public AssetIndex? AssetIndex { get; init; }
    public string? AssetIndexUrl { get; init; }
    public MinecraftFileDownload? ClientDownload { get; init; }
    public MinecraftArguments? Arguments { get; init; }
    public IReadOnlyList<MinecraftLoader> Loaders { get; init; } = [];
    public IReadOnlyList<MinecraftLibrary> Libraries { get; init; } = [];

    /// <summary>
    /// Files that are stored in the shared libraries directory but never placed on the
    /// classpath (e.g. the Forge installer jar, modlauncher runtime files).
    /// </summary>
    public IReadOnlyList<MinecraftLibrary> MavenFiles { get; init; } = [];

    public string? Jar { get; init; }
    public string? MainClass { get; init; }
    public string? MinecraftArguments { get; init; }
    public string? InheritsFrom { get; init; }

    public MinecraftVersionType Type { get; init; }
    public DateTime? ReleaseTime { get; init; }
    public IReadOnlyList<string> Tweakers { get; init; } = [];
}

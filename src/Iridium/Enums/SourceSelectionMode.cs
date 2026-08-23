namespace Iridium.Enums;

/// <summary>
/// Strategy used when a download has both an official and a mirror candidate.
/// The numeric values are stable and mirror the legacy MinecraftLaunch
/// <c>DownloadSourceMode</c> so persisted settings keep their meaning.
/// </summary>
public enum SourceSelectionMode {
    /// <summary>Latency-probe both candidates and pick the faster one.</summary>
    Auto = 0,

    /// <summary>Try the official source first, falling back to the mirror on timeout.</summary>
    OfficialPreferred = 1,

    /// <summary>Try the mirror first, falling back to the official source on timeout.</summary>
    MirrorPreferred = 2,

    /// <summary>Only ever use the official source. Mirroring is completely disabled.</summary>
    OfficialOnly = 3
}

using Iridium.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Models.Installation;

public sealed record MinecraftInstallResult {
    /// <summary>The install target this result was produced for.</summary>
    public required MinecraftTarget Target { get; init; }

    /// <summary>
    /// The resolved Minecraft context after a successful install, or <c>null</c> when the
    /// install did not reach the resolve stage.
    /// </summary>
    public MinecraftContext? Minecraft { get; init; }

    public required string VersionJsonPath { get; init; }
    public required string ClientJarPath { get; init; }
}

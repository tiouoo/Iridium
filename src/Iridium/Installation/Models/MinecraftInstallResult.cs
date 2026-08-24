using Iridium.Minecraft;

namespace Iridium.Installation.Models;

public sealed record MinecraftInstallResult {
    public required MinecraftContext Minecraft { get; init; }
    public required string VersionJsonPath { get; init; }
    public required string ClientJarPath { get; init; }
}

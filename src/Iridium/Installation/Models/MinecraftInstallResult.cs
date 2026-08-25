using Iridium.Minecraft.Models;

namespace Iridium.Installation.Models;

public sealed record MinecraftInstallResult {
    public required MinecraftEntry Entry { get; init; }
    public required string VersionJsonPath { get; init; }
    public required string ClientJarPath { get; init; }
}

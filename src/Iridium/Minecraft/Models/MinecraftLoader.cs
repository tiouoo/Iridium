using Iridium.Enums;

namespace Iridium.Minecraft.Models;

public sealed record MinecraftLoader {
    public LoaderType Type { get; init; }
    public string Version { get; init; } = string.Empty;
}
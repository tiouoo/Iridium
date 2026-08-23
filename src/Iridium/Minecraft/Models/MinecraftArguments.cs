namespace Iridium.Minecraft.Models;

public sealed record MinecraftArguments {
    public IReadOnlyList<MinecraftArgument> Game { get; init; } = [];
    public IReadOnlyList<MinecraftArgument> Jvm { get; init; } = [];
}

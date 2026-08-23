using Iridium.Enums;
using Iridium.Launch;
using Iridium.Minecraft.Models;

namespace Iridium.Launch;

public static class MinecraftArgumentParserFactory {
    public static IMinecraftArgumentParser Create(MinecraftEntry entry)
        => entry.Format == MinecraftFormat.Prism
            ? new PrismMinecraftArgumentParser()
            : new StandardMinecraftArgumentParser();
}

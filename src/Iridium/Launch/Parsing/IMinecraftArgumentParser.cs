using Iridium.Launch;
using Iridium.Launch.Models;
using Iridium.Minecraft.Models;

namespace Iridium.Launch;

public interface IMinecraftArgumentParser {
    LaunchArguments Build(MinecraftEntry entry, LaunchConfig config);
}

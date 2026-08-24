using Iridium.Launch.Models;
using Iridium.Minecraft;

namespace Iridium.Minecraft.Arguments;

public interface IArgumentParser {
    LaunchArguments Build(MinecraftContext context, LaunchConfig config);
}

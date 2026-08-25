using Iridium.Enums;

namespace Iridium.Launch;

public interface IMinecraftLayoutFactory {
    IMinecraftLayout Create(MinecraftFormat format);
}

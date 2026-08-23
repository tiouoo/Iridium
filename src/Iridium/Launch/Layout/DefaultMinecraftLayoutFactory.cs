using Iridium.Enums;
using Iridium.Launch;

namespace Iridium.Launch;

public sealed class DefaultMinecraftLayoutFactory : IMinecraftLayoutFactory {
    public IMinecraftLayout Create(MinecraftFormat format) =>
        format == MinecraftFormat.Prism ? new PrismMinecraftLayout() : new StandardMinecraftLayout();
}

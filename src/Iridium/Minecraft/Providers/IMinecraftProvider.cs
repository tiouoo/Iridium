using Iridium.Minecraft.Models;

namespace Iridium.Minecraft;

public interface IMinecraftProvider {
    Task<MinecraftEntry?> GetMinecraftAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MinecraftEntry>> GetMinecraftsAsync(CancellationToken cancellationToken = default);
}

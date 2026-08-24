namespace Iridium.Minecraft;

/// <summary>
/// Facade over the registered format providers. It auto-detects the format that handles
/// a given directory and delegates resolution to it. Consumers never touch
/// <see cref="Formats.IFormatProvider"/> directly.
/// </summary>
public interface IMinecraftProvider {
    /// <summary>
    /// Resolves a single instance whose directory is <paramref name="root"/>. Returns
    /// <c>null</c> when no registered format claims the directory.
    /// </summary>
    ValueTask<MinecraftContext?> GetAsync(DirectoryInfo root, CancellationToken ct = default);

    /// <summary>Enumerates all instances under a launcher root.</summary>
    ValueTask<IReadOnlyList<MinecraftContext>> GetMinecraftsAsync(DirectoryInfo root, CancellationToken ct = default);
}

using Iridium.Minecraft.Arguments;
using Iridium.Installation;

namespace Iridium.Minecraft.Formats;

/// <summary>
/// A concrete Minecraft format provider. It is the only component allowed to recognize
/// a format: detection, parsing and format-specific install/argument behaviour all live
/// behind this interface. Consumers of <see cref="MinecraftContext"/> must never branch
/// on the format id themselves.
/// </summary>
public interface IFormatProvider {
    string Id { get; }

    int Priority { get; }

    /// <summary>
    /// Coarse detection: whether <paramref name="root"/> looks like a launcher root of
    /// this format (e.g. a directory containing <c>versions/</c>, <c>instances/</c>, ...).
    /// </summary>
    bool CanResolve(DirectoryInfo root);

    /// <summary>
    /// Resolves a single instance when <paramref name="root"/> is itself an instance
    /// directory. Returns <c>null</c> when the root does not match this format.
    /// </summary>
    ValueTask<MinecraftContext?> TryResolveAsync(DirectoryInfo root, CancellationToken ct = default);

    /// <summary>Enumerates all instances under a launcher root of this format.</summary>
    ValueTask<IReadOnlyList<MinecraftContext>> GetMinecraftsAsync(DirectoryInfo root, CancellationToken ct = default);

    /// <summary>Adds format-specific installation operations to the install task.</summary>
    void ConfigureInstallation(InstallTaskBuilder builder, MinecraftContext context);

    /// <summary>Adds format-specific launch arguments to the argument builder.</summary>
    void ConfigureArguments(ArgumentBuilder builder, MinecraftContext context);
}

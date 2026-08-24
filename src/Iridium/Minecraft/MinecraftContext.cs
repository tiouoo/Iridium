using IFormatProvider = Iridium.Minecraft.Formats.IFormatProvider;
using Iridium.Minecraft.Layout;
using Iridium.Minecraft.Models;

namespace Iridium.Minecraft;

/// <summary>
/// A fully resolved Minecraft instance: the format that produced it, the layout it maps
/// to and the parsed entry. This is the common language shared by the provider, the
/// argument parser and the installer — after a context is produced, no component should
/// re-detect the format.
/// </summary>
public sealed record MinecraftContext {
    /// <summary>Format identifier, e.g. "Standard", "Prism", "CurseForge".</summary>
    public required string Format { get; init; }

    /// <summary>Directory mapping owned by the resolving format provider.</summary>
    public required IMinecraftLayout Layout { get; init; }

    /// <summary>Parsed instance data.</summary>
    public required MinecraftEntry Entry { get; init; }

    /// <summary>The provider that produced this context, when available.</summary>
    public IFormatProvider? Provider { get; init; }

    /// <summary>Format-specific metadata bag, used by providers when needed.</summary>
    public object? Metadata { get; init; }
}

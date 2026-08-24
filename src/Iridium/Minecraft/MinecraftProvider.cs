using Iridium.Minecraft.Formats;
using IFormatProvider = Iridium.Minecraft.Formats.IFormatProvider;

namespace Iridium.Minecraft;

/// <summary>
/// Resolves a Minecraft directory by trying each registered format provider. Providers
/// are consulted in descending <see cref="IFormatProvider.Priority"/> order.
/// </summary>
public sealed class MinecraftProvider : IMinecraftProvider {
    private readonly IReadOnlyList<IFormatProvider> _providers;

    public static readonly IReadOnlyList<IFormatProvider> Default = [
        new StandardMinecraftProvider(),
        new PrismMinecraftProvider()
    ];
    
    public MinecraftProvider(IEnumerable<IFormatProvider>? providers = null) {
        var p = providers ?? Default;
        _providers = [.. p.OrderByDescending(provider => provider.Priority)];
    }

    public async ValueTask<MinecraftContext?> GetAsync(DirectoryInfo root, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var provider in _providers)
            if (await provider.TryResolveAsync(root, ct) is { } context)
                return context;
        
        return null;
    }

    public async ValueTask<IReadOnlyList<MinecraftContext>> GetMinecraftsAsync(DirectoryInfo root, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var provider in _providers) {
            if (!provider.CanResolve(root))
                continue;
        
            return await provider.GetMinecraftsAsync(root, ct);
        }
        
        return [];
    }
}

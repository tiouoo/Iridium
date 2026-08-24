using Iridium.Minecraft;

namespace Iridium.Installation.Operations;

/// <summary>
/// Materializes the un-hashed ("virtual") asset layout consumed by legacy Minecraft
/// versions after the hashed objects have been downloaded.
/// </summary>
public sealed class ReconstructAssetsOperation : IInstallOperation {
    public string Name => "Deploy assets";
    public double Weight => 0.15;

    public async ValueTask ExecuteAsync(InstallContext context, CancellationToken ct = default) {
        var mc = context.GetState<MinecraftContext>("resolved-context")
            ?? throw new InvalidOperationException("Resolved context not found in install context.");

        await new AssetsReconstructor(mc.Layout)
            .ReconstructAsync(mc.Entry, mc.Layout.GetGameDirectory(mc.Entry), ct);
    }
}

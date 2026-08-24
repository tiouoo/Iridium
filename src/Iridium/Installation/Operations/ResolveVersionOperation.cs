using System.Text.Json;
using Iridium.Minecraft;
using Iridium.Minecraft.Models;

namespace Iridium.Installation.Operations;

/// <summary>
/// Resolves the freshly downloaded version manifest back into a full
/// <see cref="MinecraftContext"/> through the unified provider. Parent/child resolution is
/// delegated to the provider, never re-implemented here.
/// </summary>
public sealed class ResolveVersionOperation(IMinecraftProvider provider) : IInstallOperation {
    public string Name => "Parse version JSON";
    public double Weight => 0.4;

    public async ValueTask ExecuteAsync(InstallContext context, CancellationToken ct = default) {
        var seed = context.GetState<MinecraftEntry>("seed-entry")
            ?? throw new InvalidOperationException("Seed entry not found in install context.");
        var jsonPath = context.GetState<string>("version-json-path")
            ?? throw new InvalidOperationException("Version JSON path not found in install context.");

        var resolved = await provider.GetAsync(new DirectoryInfo(seed.InstancePath), ct);
        if (resolved is null) {
            // Fallback for standalone vanilla manifests: parse the manifest directly.
            var json = await File.ReadAllTextAsync(jsonPath, ct);
            using var document = JsonDocument.Parse(json);
            var entry = VersionJsonParser.MapEntry(document.RootElement, seed.Id) with {
                InstancePath = seed.InstancePath,
                MinecraftVersion = seed.Id
            };
            resolved = context.Minecraft with { Entry = entry };
        }

        context.SetState("resolved-context", resolved);
    }
}

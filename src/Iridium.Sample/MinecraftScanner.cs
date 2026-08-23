using Iridium.Interfaces.Minecraft;
using Iridium.Providers.Minecraft;
using Iridium.Sample.Providers.CurseForge;
using Iridium.Sample.Providers.Modrinth;
using Iridium.Sample.Providers.PortalMc;

namespace Iridium.Sample;

public static class MinecraftScanner {
    internal static readonly (string Name, string Path, Func<DirectoryInfo, IMinecraftProvider> Provider)[] Folders = [
        ("Portal MC", @"C:\Users\84067\AppData\Roaming\cc.tiouo.portal.minecraft", root => new PortalMcProvider(root)),
        ("Modrinth", @"C:\Users\84067\AppData\Roaming\ModrinthApp", root => new ModrinthProvider(root)),
        ("CurseForge", @"C:\Users\84067\curseforge\minecraft", root => new CurseForgeProvider(root)),
        ("Axolotl", @"C:\Users\84067\AppData\Roaming\red.ghs.axolotl", root => new ModrinthProvider(root)),
        ("BakaXL", @"C:\Users\84067\AppData\Roaming\.BakaXL\minecraft", root => new PrismMinecraftProvider(root)),
        ("MultiMC", @"D:\Temp\MultiMC", root => new PrismMinecraftProvider(root)),
        (".minecraft", @"D:\Minecraft\.minecraft", root => new StandardMinecraftProvider(root))
    ];

    public static async Task RunAsync() {
        Console.WriteLine();
        foreach (var (name, path, create) in Folders) {
            try {
                var provider = create(new DirectoryInfo(path));
                var instances = await provider.GetMinecraftsAsync();
                Console.WriteLine($"{name,-16} {instances.Count} instance(s)  ({provider.GetType().Name})");
            }
            catch (Exception exception) {
                Console.WriteLine($"{name,-16} ERROR - {exception.Message}");
            }
        }
    }
}

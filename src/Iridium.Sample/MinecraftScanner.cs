using Iridium.Extension.Minecraft.Formats;
using Iridium.Minecraft;
using Iridium.Minecraft.Formats;

namespace Iridium.Sample;

public static class MinecraftScanner {
    internal static readonly MinecraftProvider Provider = new([
        new StandardMinecraftProvider(),
        new PrismMinecraftProvider(),
        new PortalMcProvider(),
        new CurseForgeProvider(),
        new ModrinthProvider()
    ]);

    internal static readonly (string Name, string Path)[] Folders = [
        ("Portal MC", @"C:\Users\84067\AppData\Roaming\cc.tiouo.portal.minecraft"),
        ("Modrinth", @"C:\Users\84067\AppData\Roaming\ModrinthApp"),
        ("CurseForge", @"C:\Users\84067\curseforge\minecraft"),
        ("Axolotl", @"C:\Users\84067\AppData\Roaming\red.ghs.axolotl"),
        ("BakaXL", @"C:\Users\84067\AppData\Roaming\.BakaXL\minecraft"),
        ("MultiMC", @"D:\Temp\MultiMC"),
        (".minecraft", @"D:\Minecraft\.minecraft")
    ];

    public static async Task RunAsync() {
        Console.WriteLine();
        foreach (var (name, path) in Folders) {
            try {
                var instances = await Provider.GetMinecraftsAsync(new DirectoryInfo(path));
                Console.WriteLine($"{name,-16} {instances.Count} instance(s)  ({instances.FirstOrDefault()?.Format ?? "-"})");
            }
            catch (Exception exception) {
                Console.WriteLine($"{name,-16} ERROR - {exception.Message}");
            }
        }
    }
}

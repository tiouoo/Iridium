using Iridium;
using Iridium.Installation;
using Iridium.Minecraft;

IridiumConfig.Configure(new IridiumContext());

MinecraftProvider provider = new();

var mc = await provider.GetAsync(new DirectoryInfo("/home/yang429/.local/share/PrismLauncher/instances/黄铜协奏曲"));

Console.WriteLine($"Format: {mc.Format}");
Console.WriteLine($"Id: {mc.Entry.Id}");
Console.WriteLine($"{mc.Metadata}");

// VanillaInstaller installer = new();
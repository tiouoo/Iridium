using Iridium;
using Iridium.Models;

IridiumConfig.Configure(new IridiumContext());
Console.OutputEncoding = System.Text.Encoding.UTF8;

while (true)
{
    await Iridium.Sample.MinecraftLauncher.RunAsync();
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
}

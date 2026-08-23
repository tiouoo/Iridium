using Iridium;
using Iridium.Models;

IridiumConfig.Configure(new IridiumContext());
Console.OutputEncoding = System.Text.Encoding.UTF8;

await Iridium.Sample.MinecraftScanner.RunAsync();

Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

using Iridium;
using Iridium.Authentication;
using Iridium.Installation.Installer;
using Iridium.Java;
using Iridium.Launch;
using Iridium.Models.Authentication;
using Iridium.Models.Launch;
using Iridium.Models.Minecraft;

IridiumConfig.Configure(new IridiumContext());

const string versionId = "1.20.1";
var gameRoot = new DirectoryInfo("/home/yang429/文档/mc/.minecraft");

var versions = await VanillaInstaller.GetVersionsAsync();
var version = versions?.FirstOrDefault(x => x.Id == versionId);

var installer = new VanillaInstaller(MinecraftTarget.Create(gameRoot));

installer.ProgressChanged += (sender, eventArgs) => {
    Console.Clear();

    Console.WriteLine($"Total: {eventArgs.Progress.Progress * 100:0.00}% -- {eventArgs.Progress.CompletedUnits}/{eventArgs.Progress.TotalUnits}");
    foreach (var r in eventArgs.Progress.Steps)
        Console.WriteLine($"[{r.Status}] {r.Name} -- {r.Completed}/{r.Total}");
};
    

var result = await installer.InstallAsync(version!);

Console.WriteLine(result.ClientJarPath);
Console.WriteLine(result.VersionJsonPath);

var launcher = new Launcher();
var provider = new JavaProvider();

await foreach(var java in provider.EnumerableJavaAsync())
    Console.WriteLine($"{java.JavaPath} -- {java.MajorVersion}");

var process = await launcher.LaunchAsync(result.Minecraft, new LaunchConfig {
    Account = new OfflineAuthenticator().Authenticate("Offline429"),
    JavaPath = await provider.GetJavaEntryAsync(Console.ReadLine()),
    LauncherName = "Test"
});

Console.WriteLine();
Console.WriteLine(string.Join('\n', process.ArgumentList));
Console.WriteLine();

process.OutputLogReceived += (sender, args) =>
    Console.WriteLine(args.Data);

Console.ReadKey();
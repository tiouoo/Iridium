using Iridium.Extensions;
using Iridium.Launch;
using Iridium.Models.Authentication;
using Iridium.Models.Java;
using Iridium.Models.Launch;
using Iridium.Providers.Java;
using Iridium.Providers.Minecraft;

namespace Iridium.Sample;

public static class MinecraftLauncher
{
    private static readonly IReadOnlyDictionary<string, string> JavaVersionDefaultPaths =
        new Dictionary<string, string>
        {
            ["25"] = @"C:\Program Files\Microsoft\jdk-25.0.3.9-hotspot\bin\java.exe",
            ["8"] = @"C:\Users\84067\AppData\Roaming\cc.tiouo.Portal\Runtimes\Java\Azul-8\bin\java.exe",
            ["17"] =
                @"C:\Users\84067\AppData\Roaming\ModrinthApp\meta\java_versions\zulu17.66.19-ca-jre17.0.19-win_x64\bin\java.exe",
            ["21"] = @"C:\Users\84067\curseforge\minecraft\Install\java\Jre_21\bin\java.exe"
        };

    public static async Task RunAsync()
    {
        var folders = MinecraftScanner.Folders;

        Console.WriteLine();
        Console.WriteLine("选择要测试的启动目录:");
        for (var i = 0; i < folders.Length; i++)
            Console.WriteLine($"  [{i + 1,2}] {folders[i].Name,-16} {folders[i].Path}");

        Console.WriteLine("输入序号选择目录:");
        Console.WriteLine("");
        if (!int.TryParse(Console.ReadLine(), out var folderChoice) ||
            folderChoice < 1 || folderChoice > folders.Length)
        {
            Console.WriteLine("无效的选择。");
            return;
        }

        var (name, path, create) = folders[folderChoice - 1];
        var provider = create(new DirectoryInfo(path));
        var instances = await provider.GetMinecraftsAsync();

        Console.WriteLine();
        Console.WriteLine($"扫描实例 ({name})...");
        if (instances.Count == 0)
        {
            Console.WriteLine("未找到任何实例。");
            return;
        }

        Console.WriteLine($"找到 {instances.Count} 个实例:");
        for (var i = 0; i < instances.Count; i++)
        {
            var entry = instances[i];
            var loadersText = string.Join(",", entry.Loaders.Select(loader => $"{loader.Type} {loader.Version}"));
            Console.WriteLine(
                $"  [{i + 1,3}] {entry.Name,-50} " +
                $"MC {entry.MinecraftVersion,-22} {(string.IsNullOrWhiteSpace(loadersText) ? "Vallian" : loadersText),-22} " +
                $"Java {entry.RequiredJavaVersion?.ToString() ?? "?"}");
        }

        Console.WriteLine("输入序号选择要启动的实例:");
        Console.WriteLine("");
        if (!int.TryParse(Console.ReadLine(), out var choice) || choice < 1 || choice > instances.Count)
        {
            Console.WriteLine("无效的选择。");
            return;
        }

        var selected = instances[choice - 1];

        Console.WriteLine("解析默认 Java...");
        var javas = new List<JavaEntry>();
        foreach (var (_, javaPath) in JavaVersionDefaultPaths)
        {
            if (await new JavaProvider().GetJavaEntryAsync(javaPath) is { } java)
                javas.Add(java);
            else
                Console.WriteLine($"  跳过无效路径: {javaPath}");
        }

        Console.WriteLine("自动选择合适的 Java...");
        var selectedJava = await selected.SelectAppropriateJavaAsync(javas);
        if (selectedJava is null)
        {
            Console.WriteLine("没有可用的 Java 运行时。");
            return;
        }

        Console.WriteLine($"已选择: {selectedJava}");

        var config = new LaunchConfig
        {
            Account = new OfflineAccount("ttt", Guid.NewGuid(), Guid.NewGuid().ToString("N")),
            JavaPath = selectedJava,
            MaxMemorySize = 8192,
            MinMemorySize = 512
        };

        Console.WriteLine("启动中...");
        try
        {
            await ResourcePreparer.EnsureAsync(selected);
            Console.WriteLine();

            var process = await new Launcher().LaunchAsync(selected, config);
            Console.WriteLine($"进程已启动 (PID: {process.Process!.Id})");

            process.OutputLogReceived += (_, e) => Console.WriteLine($"[MC] {e.Data}");
            process.Exited += (_, _) => Console.WriteLine("[MC] 进程已退出。");

            await process.Process.WaitForExitAsync();
            process.Dispose();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"启动失败: {exception.Message}");
        }
    }
}
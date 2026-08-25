using System.Diagnostics;
using Iridium.Download;
using Iridium.Installation;
using Iridium.Models.Minecraft;

namespace Iridium.Sample;

/// <summary>
/// Verifies that the install DAG scheduler runs independent steps in parallel using the
/// new <c>Do</c>/<c>After</c> DSL. Inserting Step2 then Step3 after Step1 makes Step3 the
/// pipeline gate, so Step2 runs in parallel with Step3 → Step4. Expected total ~11s; a fully
/// sequential run would be 16s.
/// </summary>
public static class TestInstaller {
    public static InstallTask CreateTask() =>
        InstallTask.Define(task => {
            task.Do("Step1", (context, progress, ct) => WaitAsync("Step1", 3, progress, ct));

            task.After("Step1", "Step2", (context, progress, ct) => WaitAsync("Step2", 5, progress, ct));
            task.After("Step1", "Step3", (context, progress, ct) => WaitAsync("Step3", 3, progress, ct));
            task.After("Step3", "Step4", (context, progress, ct) => WaitAsync("Step4", 5, progress, ct));
        });

    public static async Task RunAsync() {
        var task = CreateTask();
        var target = MinecraftTarget.Create(new DirectoryInfo(Path.GetTempPath()));
        var installContext = new InstallContext {
            Target = target,
            Source = DownloadSource.Official
        };

        Console.WriteLine("== TestInstaller: DAG 并行验证 ==");
        var stopwatch = Stopwatch.StartNew();
        var result = await InstallTaskExecutor.Default.ExecuteAsync(task, installContext);
        stopwatch.Stop();

        Console.WriteLine($"TestInstaller: success={result.IsSuccess} elapsed={stopwatch.Elapsed.TotalSeconds:F1}s (期望 ~11s，全顺序 16s)");
    }

    private static async ValueTask WaitAsync(string name, int seconds, IProgress<InstallStepProgress> progress, CancellationToken ct) {
        progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] {name} start");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] {name} end");
        progress.Report(new InstallStepProgress { Completed = 1, Total = 1 });
    }
}

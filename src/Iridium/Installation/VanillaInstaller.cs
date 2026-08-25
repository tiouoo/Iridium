using System.Text.Json;
using Flurl.Http;
using Iridium.Download;
using Iridium.Models.Installation;
using Iridium.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Installation;

public sealed class VanillaInstaller : InstallerBase {
    private const string VersionManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    
    private readonly MinecraftTarget _target;
    private readonly DownloadSource _source;

    public VanillaInstaller(MinecraftTarget target, DownloadSource? source = null) {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _source = source ?? DownloadSource.Official;
    }

    /// <summary>
    /// Builds the vanilla installation task: download manifest → resolve → download resources →
    /// deploy assets. Special installers extend the returned task with
    /// <see cref="InstallTask.Then(IInstallStep)"/> / <see cref="InstallTask.After(string, IInstallStep)"/> /
    /// <see cref="InstallTask.Before(string, IInstallStep)"/>, or combine tasks with
    /// <see cref="InstallTask.Combine"/>.
    /// </summary>
    public static InstallTask CreateTask(VersionManifestEntry version) {
        ArgumentNullException.ThrowIfNull(version);

        return InstallTask.Define(task => {
            task
                .Do("version-json", "Download Version", (context, progress, ct) => DownloadVersionAsync(version, context, progress, ct))
                .Then("resolve", "Resolve Version", ResolveVersionAsync)
                .Then("resources", "Download Resources", DownloadResourcesAsync)
                .Then("assets", "Reconstruct Assets", ReconstructAssetsAsync);
        });
    }

    /// <summary>
    /// Installs <paramref name="version"/>. <paramref name="configure"/> can append or insert
    /// special steps into the generated task; <paramref name="progress"/> receives the full
    /// step-wise snapshot (the <see cref="InstallerBase.ProgressChanged"/> event also fires).
    /// </summary>
    public async Task<MinecraftInstallResult> InstallAsync(
        VersionManifestEntry version,
        Action<InstallTask>? configure = null,
        IProgress<InstallProgress>? progress = null,
        int maxConcurrency = 32,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(version);

        var task = CreateTask(version);
        configure?.Invoke(task);

        return await ExecuteAsync(task, progress, maxConcurrency, ct);
    }

    public async Task<MinecraftInstallResult> InstallAsync(
        InstallTask task,
        IProgress<InstallProgress>? progress = null,
        int maxConcurrency = 32,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(task);

        return await ExecuteAsync(task, progress, maxConcurrency, ct);
    }

    public static async Task<IReadOnlyList<VersionManifestEntry>?> GetVersionsAsync(CancellationToken ct = default) {
        await using var stream = await VersionManifestUrl
            .GetStreamAsync(HttpCompletionOption.ResponseContentRead, ct);

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        
        var versions = document.RootElement
            .GetProperty("versions")
            .Deserialize<IEnumerable<VersionManifestEntry>>(
                VersionManifestEntryContext.Default.IEnumerableVersionManifestEntry);

        ArgumentNullException.ThrowIfNull(versions);
        return [.. versions];
    }

    private async Task<MinecraftInstallResult> ExecuteAsync(
        InstallTask task,
        IProgress<InstallProgress>? progress,
        int maxConcurrency,
        CancellationToken ct) {
        var installContext = new InstallContext {
            Target = _target,
            Source = _source
        };

        var result = await InstallTaskExecutor.Default.ExecuteAsync(
            task,
            installContext,
            maxConcurrency,
            new Progress<InstallProgress>(p => {
                progress?.Report(p);
                ReportProgress(p);
            }),
            ct);

        ReportCompleted(result.IsSuccess, result.Failures.FirstOrDefault());

        var resolved = installContext.GetState<MinecraftContext>("resolved-context");
        var entry = resolved?.Entry
            ?? installContext.GetState<MinecraftEntry>("seed-entry")
            ?? new MinecraftEntry { InstancePath = _target.Root.FullName };

        return new MinecraftInstallResult {
            Target = _target,
            Minecraft = resolved,
            VersionJsonPath = installContext.GetState<string>("version-json-path") ?? string.Empty,
            ClientJarPath = _target.Layout.GetVersionJarPath(entry)
        };
    }

    private static async ValueTask DownloadVersionAsync(
        VersionManifestEntry version,
        InstallContext context,
        IProgress<InstallStepProgress> progress,
        CancellationToken ct) {
        progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });

        var layout = context.Target.Layout;
        // MinecraftTarget.Root is the Game Root; the instance directory is derived by the
        // layout from the instance/version id.
        var instancePath = Path.Combine(
            context.Target.Root.FullName,
            layout.GetInstanceDirectory(version.Id));
        var seed = new MinecraftEntry {
            Id = version.Id,
            Name = version.Id,
            InstancePath = instancePath
        };
        var jsonPath = layout.GetVersionJsonPath(seed);

        context.SetState("seed-entry", seed);
        context.SetState("version-json-path", jsonPath);

        await using var input = await version.Url
            .GetStreamAsync(HttpCompletionOption.ResponseContentRead, ct);

        var jsonFile = new FileInfo(jsonPath);
        jsonFile.Directory?.Create();

        await using var output = new FileStream(
            jsonFile.FullName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await input.CopyToAsync(output, ct);

        progress.Report(new InstallStepProgress { Completed = 1, Total = 1 });
    }

    private static async ValueTask ResolveVersionAsync(
        InstallContext context,
        IProgress<InstallStepProgress> progress,
        CancellationToken ct) {
        progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });

        var seed = context.GetState<MinecraftEntry>("seed-entry")
            ?? throw new InvalidOperationException("Seed entry not found in install context.");
        var jsonPath = context.GetState<string>("version-json-path")
            ?? throw new InvalidOperationException("Version JSON path not found in install context.");

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        using var document = JsonDocument.Parse(json);
        var entry = VersionJsonParser.MapEntry(document.RootElement, seed.Id) with {
            InstancePath = seed.InstancePath,
            MinecraftVersion = seed.Id
        };

        context.SetState("resolved-context", new MinecraftContext {
            Format = context.Target.Format,
            Layout = context.Target.Layout,
            Entry = entry
        });

        progress.Report(new InstallStepProgress { Completed = 1, Total = 1 });
    }

    private static async ValueTask DownloadResourcesAsync(
        InstallContext context,
        IProgress<InstallStepProgress> progress,
        CancellationToken ct) {
        var mc = context.GetState<MinecraftContext>("resolved-context")
            ?? throw new InvalidOperationException("Resolved context not found in install context.");

        using var downloader = context.CreateResourceDownloader(mc.Layout);
        downloader.ProgressChanged += (_, args) =>
            progress.Report(new InstallStepProgress { Completed = args.CompletedCount, Total = args.TotalCount });

        var result = await downloader.DownloadAsync(mc.Entry, ct);
        if (result.FailCount > 0)
            throw new InvalidOperationException(
                $"Some dependent files encountered errors during download. FailCount: {result.FailCount}");
    }

    private static async ValueTask ReconstructAssetsAsync(
        InstallContext context,
        IProgress<InstallStepProgress> progress,
        CancellationToken ct) {
        progress.Report(new InstallStepProgress { Completed = 0, Total = 1 });

        var mc = context.GetState<MinecraftContext>("resolved-context")
            ?? throw new InvalidOperationException("Resolved context not found in install context.");

        await new AssetsReconstructor(mc.Layout)
            .ReconstructAsync(mc.Entry, mc.Layout.GetGameDirectory(mc.Entry), ct);

        progress.Report(new InstallStepProgress { Completed = 1, Total = 1 });
    }
}

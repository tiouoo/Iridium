using Iridium.Download;
using Iridium.Installation.Models;
using Iridium.Installation.Operations;
using Iridium.Minecraft;
using Iridium.Minecraft.Layout;
using Iridium.Minecraft.Models;

namespace Iridium.Installation;

/// <summary>
/// Builds and executes install tasks. The installer does not perform installation itself:
/// <see cref="CreateTask(MinecraftContext, VersionManifestEntry)"/> describes the work as a
/// DAG of operations, and <see cref="InstallAsync(MinecraftContext, VersionManifestEntry, CancellationToken)"/>
/// hands it to the <see cref="InstallTaskExecutor"/>. Format-specific behavior is contributed
/// through <c>context.Provider.ConfigureInstallation</c>.
/// </summary>
public sealed class VanillaInstaller {
    private readonly IMinecraftProvider _provider;
    private readonly DownloadSource _source;
    private readonly int _maxConcurrency;

    public event EventHandler<InstallerCompletedEventArgs>? Completed;
    public event EventHandler<InstallProgressChangedEventArgs>? ProgressChanged;

    public VanillaInstaller(IMinecraftProvider provider, DownloadSource? source = null, int maxConcurrency = 32) {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _source = source ?? DownloadSource.Official;
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    /// <summary>
    /// Builds the vanilla install task: download manifest → resolve → download resources →
    /// deploy assets, then lets the context's format provider contribute extra operations.
    /// </summary>
    public InstallTask CreateTask(MinecraftContext context, VersionManifestEntry version) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(version);

        var builder = new InstallTaskBuilder();
        var downloadJson = builder.Add(new DownloadVersionJsonOperation(version), key: "version-json");
        var resolve = builder.Add(new ResolveVersionOperation(_provider), downloadJson, key: "resolve");
        var resources = builder.Add(new DownloadResourcesOperation(), resolve, key: "resources");
        builder.Add(new ReconstructAssetsOperation(), resources, key: "assets");

        context.Provider?.ConfigureInstallation(builder, context);
        return builder.Build();
    }

    public async Task<MinecraftInstallResult> InstallAsync(
        MinecraftContext context,
        VersionManifestEntry version,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(version);

        var task = CreateTask(context, version);
        using var downloader = new DefaultDownloader(_maxConcurrency);
        var installContext = new InstallContext {
            Minecraft = context,
            Source = _source,
            Downloader = downloader
        };

        var executor = new InstallTaskExecutor(4);
        var result = await executor.ExecuteAsync(task, installContext, progress: new Progress<InstallProgress>(ReportProgress), ct);

        ReportCompleted(result.IsSuccess, result.Failures.FirstOrDefault());

        return new MinecraftInstallResult {
            Minecraft = context,
            VersionJsonPath = installContext.GetState<string>("version-json-path") ?? string.Empty,
            ClientJarPath = context.Layout.GetVersionJarPath(context.Entry)
        };
    }

    /// <summary>
    /// High-level convenience: resolves the root through the provider (for an existing
    /// instance) or falls back to a fresh vanilla/standard install, then builds and
    /// executes the task.
    /// </summary>
    public async Task<MinecraftInstallResult> InstallAsync(
        DirectoryInfo root,
        VersionManifestEntry version,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(version);

        var context = await _provider.GetAsync(root, ct);
        if (context is null) {
            var instancePath = Path.Combine(root.FullName, "versions", version.Id);
            context = new MinecraftContext {
                Format = "Standard",
                Layout = new StandardLayout(),
                Entry = new MinecraftEntry {
                    Id = version.Id,
                    Name = version.Id,
                    InstancePath = instancePath
                }
            };
        }

        return await InstallAsync(context, version, ct);
    }

    private void ReportProgress(InstallProgress progress) =>
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs(progress));

    private void ReportCompleted(bool isSuccess, Exception? exception) =>
        Completed?.Invoke(this, new InstallerCompletedEventArgs(isSuccess, exception));
}
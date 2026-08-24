using Iridium.Download;
using Iridium.Minecraft;

namespace Iridium.Installation.Operations;

/// <summary>
/// Downloads the client jar, libraries and assets referenced by the resolved entry. Reuses
/// the shared <see cref="DefaultDownloader"/> owned by the executor so the whole install
/// respects a single global concurrency budget.
/// </summary>
public sealed class DownloadResourcesOperation : IInstallOperation {
    public string Name => "Download game resources";
    public double Weight => 0.4;

    public async ValueTask ExecuteAsync(InstallContext context, CancellationToken ct = default) {
        var mc = context.GetState<MinecraftContext>("resolved-context")
            ?? throw new InvalidOperationException("Resolved context not found in install context.");

        using var downloader = new ResourceDownloader(context.Downloader, context.Source, mc.Layout);
        downloader.ProgressChanged += (_, args) => context.ReportProgress(
            args.TotalCount > 0 ? args.CompletedCount / (double)args.TotalCount : 0d);

        var result = await downloader.DownloadAsync(mc.Entry, ct);
        if (result.FailCount > 0)
            throw new InvalidOperationException(
                $"Some dependent files encountered errors during download. FailCount: {result.FailCount}");
    }
}

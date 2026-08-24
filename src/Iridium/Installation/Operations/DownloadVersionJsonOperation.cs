using Flurl.Http;
using Iridium.Installation.Models;
using Iridium.Minecraft.Models;

namespace Iridium.Installation.Operations;

/// <summary>
/// Downloads the version manifest JSON of the requested version into its layout location.
/// </summary>
public sealed class DownloadVersionJsonOperation(VersionManifestEntry version) : IInstallOperation {
    public string Name => "Download version JSON";
    public double Weight => 0.05;

    public async ValueTask ExecuteAsync(InstallContext context, CancellationToken ct = default) {
        var layout = context.Minecraft.Layout;
        var seed = context.Minecraft.Entry with { Id = version.Id };
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
    }
}

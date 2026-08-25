namespace Iridium.Installation.Tasks;

public delegate ValueTask InstallStepHandler(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct);

public interface IInstallStep {
    InstallStepKey Key { get; }

    string Name { get; }

    ValueTask ExecuteAsync(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct = default);
}

public sealed class InstallStep(InstallStepKey key, string name, InstallStepHandler handler) : IInstallStep {
    public InstallStepKey Key => key;
    public string Name => name;

    public ValueTask ExecuteAsync(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct = default) =>
        handler(context, progress, ct);
}

internal sealed record InstallStepNode {
    public required InstallStepKey Key { get; init; }
    public required IInstallStep Step { get; init; }
    public IReadOnlyList<InstallStepKey> DependsOn { get; init; } = [];
}
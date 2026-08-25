namespace Iridium.Installation;

/// <summary>
/// Execution contract of a delegate-based step. Simple steps should be expressed as a
/// <see cref="InstallTask.Do(IInstallStep)"/>/<see cref="InstallTask.Then(IInstallStep)"/>
/// delegate; only complex, reused or stateful steps need a dedicated <see cref="IInstallStep"/>
/// class.
/// </summary>
public delegate ValueTask InstallStepHandler(
    InstallContext context,
    IProgress<InstallStepProgress> progress,
    CancellationToken ct);

/// <summary>
/// A logical stage in an install task (download a version, resolve a manifest, install a
/// loader, apply overrides, ...). A step carries a stable internal <see cref="Id"/> (used for
/// task dependencies and progress) separate from its display <see cref="Name"/>. A step
/// reports only its own local progress; the <see cref="InstallTaskExecutor"/> aggregates the
/// full task snapshot.
/// </summary>
public interface IInstallStep {
    /// <summary>Stable step identity, used as the task dependency key.</summary>
    string Id { get; }

    /// <summary>Human-readable step name shown in progress snapshots.</summary>
    string Name { get; }

    ValueTask ExecuteAsync(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct = default);
}

/// <summary>
/// Adapts an <see cref="InstallStepHandler"/> delegate to <see cref="IInstallStep"/> so the
/// DSL can treat delegate steps and class-based steps uniformly.
/// </summary>
public sealed class DelegateInstallStep(string id, string name, InstallStepHandler handler) : IInstallStep {
    public string Id => id;
    public string Name => name;

    public ValueTask ExecuteAsync(InstallContext context, IProgress<InstallStepProgress> progress, CancellationToken ct = default) =>
        handler(context, progress, ct);
}

/// <summary>A node in the install DAG: a step plus its explicit dependencies.</summary>
internal sealed record InstallStepNode {
    public required string Key { get; init; }
    public required IInstallStep Step { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}

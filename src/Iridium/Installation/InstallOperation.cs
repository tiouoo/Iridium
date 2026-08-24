namespace Iridium.Installation;

/// <summary>
/// A single concrete installation action. An operation performs exactly one step
/// (download a file, extract an archive, write metadata, ...) and never drives the
/// surrounding flow — ordering and parallelism are handled by the
/// <see cref="InstallTaskExecutor"/>.
/// </summary>
public interface IInstallOperation {
    string Name { get; }

    /// <summary>Relative weight used for progress aggregation (0..1 typical).</summary>
    double Weight { get; }

    ValueTask ExecuteAsync(InstallContext context, CancellationToken ct = default);
}

/// <summary>A node in the install DAG: an operation plus its explicit dependencies.</summary>
public sealed record InstallOperationNode {
    public required string Key { get; init; }
    public required IInstallOperation Operation { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}

/// <summary>
/// Reference returned by <see cref="InstallTaskBuilder.Add(IInstallOperation, string?)"/>
/// used to declare dependencies between operations.
/// </summary>
public sealed class InstallOperationHandle {
    public string Key { get; }

    internal List<string> Dependencies { get; } = [];

    internal InstallOperationHandle(string key) => Key = key;

    public void DependsOn(InstallOperationHandle dependency) {
        if (dependency is null || dependency.Key == Key)
            return;

        if (!Dependencies.Contains(dependency.Key, StringComparer.Ordinal))
            Dependencies.Add(dependency.Key);
    }
}

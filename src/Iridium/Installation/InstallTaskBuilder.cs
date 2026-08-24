namespace Iridium.Installation;

/// <summary>
/// Builds an <see cref="InstallTask"/>: adds operations, declares dependencies, validates
/// the resulting graph and finally produces the task.
/// </summary>
public sealed class InstallTaskBuilder {
    private static int _keyCounter;

    private readonly Dictionary<string, InstallOperationNode> _nodes = new(StringComparer.Ordinal);

    public InstallOperationHandle Add(IInstallOperation operation, string? key = null) {
        ArgumentNullException.ThrowIfNull(operation);

        var handle = new InstallOperationHandle(key ?? GenerateKey());
        if (_nodes.ContainsKey(handle.Key))
            throw new InvalidOperationException($"Install task already contains operation '{handle.Key}'.");

        _nodes.Add(handle.Key, new InstallOperationNode {
            Key = handle.Key,
            Operation = operation,
            DependsOn = handle.Dependencies
        });
        return handle;
    }

    public InstallOperationHandle Add(IInstallOperation operation, InstallOperationHandle? dependsOn) {
        var handle = Add(operation);
        if (dependsOn is not null)
            handle.DependsOn(dependsOn);
        return handle;
    }

    public InstallOperationHandle Add(IInstallOperation operation, InstallOperationHandle? dependsOn, string? key = null) {
        var handle = Add(operation, key);
        if (dependsOn is not null)
            handle.DependsOn(dependsOn);
        return handle;
    }

    public InstallOperationHandle Add(IInstallOperation operation, IReadOnlyList<InstallOperationHandle> dependsOn, string? key = null) {
        var handle = Add(operation, key);
        foreach (var dependency in dependsOn)
            handle.DependsOn(dependency);
        return handle;
    }

    /// <summary>Merges a child task into the current graph, preserving its internal relations.</summary>
    public void Add(InstallTask childTask) {
        ArgumentNullException.ThrowIfNull(childTask);
        foreach (var node in childTask.Nodes) {
            if (_nodes.ContainsKey(node.Key))
                throw new InvalidOperationException($"Install task already contains operation '{node.Key}'.");
            _nodes.Add(node.Key, node);
        }
    }

    public InstallTask Build() {
        foreach (var node in _nodes.Values)
            foreach (var dependency in node.DependsOn)
                if (!_nodes.ContainsKey(dependency))
                    throw new InvalidOperationException(
                        $"Operation '{node.Key}' depends on undefined operation '{dependency}'.");

        EnsureAcyclic();
        return new InstallTask(_nodes.Values.ToList());
    }

    private void EnsureAcyclic() {
        const int visiting = 1, visited = 2;
        var marks = new Dictionary<string, int>(StringComparer.Ordinal);

        void Dfs(string key) {
            if (marks.GetValueOrDefault(key) == visited)
                return;
            if (marks.GetValueOrDefault(key) == visiting)
                throw new InvalidOperationException($"Install task contains a dependency cycle involving '{key}'.");

            marks[key] = visiting;
            foreach (var dependency in _nodes[key].DependsOn)
                Dfs(dependency);
            marks[key] = visited;
        }

        foreach (var key in _nodes.Keys)
            Dfs(key);
    }

    private string GenerateKey() => $"op-{Interlocked.Increment(ref _keyCounter)}";
}

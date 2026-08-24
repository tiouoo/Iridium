namespace Iridium.Installation;

/// <summary>
/// An installation task to be executed: the set of operations and their dependency relations.
/// Not a linear pipeline — operations with no transitive ordering may run in parallel.
/// </summary>
public sealed class InstallTask {
    public IReadOnlyList<InstallOperationNode> Nodes { get; }

    public bool IsEmpty => Nodes.Count == 0;

    internal InstallTask(IReadOnlyList<InstallOperationNode> nodes) =>
        Nodes = nodes;

    /// <summary>
    /// Merges multiple child tasks into a single DAG. Duplicate keys are kept from the
    /// first task that declares them.
    /// </summary>
    public static InstallTask Combine(params InstallTask[] tasks) {
        var nodes = new List<InstallOperationNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in tasks) {
            ArgumentNullException.ThrowIfNull(task);
            nodes.AddRange(task.Nodes.Where(node => seen.Add(node.Key)));
        }
        
        return new InstallTask(nodes);
    }
}

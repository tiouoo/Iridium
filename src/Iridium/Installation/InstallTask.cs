namespace Iridium.Installation;

/// <summary>
/// An installation task to be executed: the ordered set of steps and their dependency relations.
/// Not a linear pipeline — steps with no transitive ordering may run in parallel.
///
/// A task is built with the fluent DSL and can be extended in place, so special install
/// flows are ordinary task composition:
/// <code>
/// var task = installer.CreateTask(version)
///     .Then("Install Forge", InstallForgeAsync)
///     .After("resolve", "Special Processing", SpecialProcessingAsync);
/// </code>
/// </summary>
public sealed class InstallTask {
    private static int _keyCounter;

    private readonly List<InstallStepNode> _nodes = [];
    private List<string> _frontier = [];

    internal IReadOnlyList<InstallStepNode> Nodes => _nodes;

    internal InstallTask() {
    }

    private InstallTask(IReadOnlyList<InstallStepNode> nodes) {
        _nodes.AddRange(nodes);
        _frontier = Leaves();
    }

    /// <summary>
    /// Describes an installation task with the fluent DSL:
    /// <code>
    /// InstallTask.Define(task =&gt; {
    ///     task
    ///         .Do("Download Version", DownloadVersionAsync)
    ///         .Then("Resolve Version", ResolveVersionAsync);
    /// });
    /// </code>
    /// Sequential chains use <c>Do</c>/<c>Then</c>; special steps use
    /// <c>After</c>/<c>Before</c> to insert at a specific step.
    /// </summary>
    public static InstallTask Define(Action<InstallTask> configure) {
        ArgumentNullException.ThrowIfNull(configure);

        var task = new InstallTask();
        configure(task);
        return task;
    }

    /// <summary>
    /// Merges multiple child tasks into a single DAG. Duplicate keys are kept from the first
    /// task that declares them. Cross-task ordering is expressed with ordinary step
    /// dependencies; independent branches run in parallel.
    /// </summary>
    public static InstallTask Combine(params InstallTask[] tasks) {
        var nodes = new List<InstallStepNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in tasks) {
            ArgumentNullException.ThrowIfNull(task);
            nodes.AddRange(task._nodes.Where(node => seen.Add(node.Key)));
        }

        return new InstallTask(nodes);
    }

    /// <summary>Adds an independent step (a new root of the DAG).</summary>
    public InstallTask Do(string name, InstallStepHandler handler) =>
        Do(new DelegateInstallStep(name, name, handler));

    /// <summary>Adds an independent step with a stable id separate from its display name.</summary>
    public InstallTask Do(string id, string name, InstallStepHandler handler) =>
        Do(new DelegateInstallStep(id, name, handler));

    /// <summary>Adds an independent class-based step; see the delegate overloads.</summary>
    public InstallTask Do(IInstallStep step) {
        Add(step, []);
        return this;
    }

    /// <summary>Adds a step that runs after the current frontier (the steps added most recently).</summary>
    public InstallTask Then(string name, InstallStepHandler handler) =>
        Then(new DelegateInstallStep(name, name, handler));

    /// <summary>Adds a step with a stable id that runs after the current frontier.</summary>
    public InstallTask Then(string id, string name, InstallStepHandler handler) =>
        Then(new DelegateInstallStep(id, name, handler));

    /// <summary>Adds a class-based step that runs after the current frontier.</summary>
    public InstallTask Then(IInstallStep step) {
        Add(step, _frontier);
        return this;
    }

    /// <summary>
    /// Inserts a step immediately after the step identified by <paramref name="dependsOn"/>.
    /// Everything that previously ran after that step now waits for the inserted step too, so
    /// the pipeline becomes <c>dependsOn → new step → former successors</c>.
    /// </summary>
    public InstallTask After(string dependsOn, string name, InstallStepHandler handler) =>
        After(dependsOn, new DelegateInstallStep(name, name, handler));

    /// <summary>Inserts a step with a stable id immediately after the step identified by <paramref name="dependsOn"/>.</summary>
    public InstallTask After(string dependsOn, string id, string name, InstallStepHandler handler) =>
        After(dependsOn, new DelegateInstallStep(id, name, handler));

    /// <summary>Inserts a class-based step immediately after the step identified by <paramref name="dependsOn"/>.</summary>
    public InstallTask After(string dependsOn, IInstallStep step) {
        Require(dependsOn);

        var key = Add(step, [dependsOn]);

        // Re-route: every step that previously depended on `dependsOn` now waits for the new step,
        // so it is inserted into the linear pipeline rather than added as a sibling.
        for (var i = 0; i < _nodes.Count; i++) {
            var node = _nodes[i];
            if (node.Key == key ||
                !node.DependsOn.Contains(dependsOn, StringComparer.Ordinal) ||
                node.DependsOn.Contains(key, StringComparer.Ordinal))
                continue;

            _nodes[i] = node with { DependsOn = [.. node.DependsOn, key] };
        }

        return this;
    }

    /// <summary>
    /// Inserts a step immediately before the step identified by <paramref name="followedBy"/>.
    /// The new step waits for everything <paramref name="followedBy"/> waited for, and
    /// <paramref name="followedBy"/> now waits for the new step.
    /// </summary>
    public InstallTask Before(string followedBy, string name, InstallStepHandler handler) =>
        Before(followedBy, new DelegateInstallStep(name, name, handler));

    /// <summary>Inserts a step with a stable id immediately before the step identified by <paramref name="followedBy"/>.</summary>
    public InstallTask Before(string followedBy, string id, string name, InstallStepHandler handler) =>
        Before(followedBy, new DelegateInstallStep(id, name, handler));

    /// <summary>Inserts a class-based step immediately before the step identified by <paramref name="followedBy"/>.</summary>
    public InstallTask Before(string followedBy, IInstallStep step) {
        Require(followedBy);

        var followed = _nodes.First(node => node.Key == followedBy);
        var key = Add(step, followed.DependsOn);
        AddDependency(followedBy, key);

        return this;
    }

    /// <summary>
    /// Fans out several steps, each running after the current frontier. A subsequent
    /// <see cref="Then(IInstallStep)"/> waits on all of them (join point).
    /// </summary>
    public InstallTask Parallel(params (string Name, InstallStepHandler Handler)[] steps) =>
        Parallel([.. steps.Select(static step => (IInstallStep)new DelegateInstallStep(step.Name, step.Name, step.Handler))]);

    /// <summary>Fans out several class-based steps; see the tuple overload.</summary>
    public InstallTask Parallel(params IInstallStep[] steps) {
        if (steps.Length == 0)
            return this;

        var keys = new string[steps.Length];
        for (var i = 0; i < steps.Length; i++)
            keys[i] = Add(steps[i], _frontier);

        _frontier = [.. keys];
        return this;
    }

    internal void Validate() {
        foreach (var node in _nodes)
            foreach (var dependency in node.DependsOn)
                if (_nodes.All(n => n.Key != dependency))
                    throw new InvalidOperationException($"Step '{node.Key}' depends on undefined step '{dependency}'.");

        EnsureAcyclic();
    }

    private string Add(IInstallStep step, IReadOnlyList<string> dependencies) {
        ArgumentNullException.ThrowIfNull(step);

        var key = ReserveKey(step.Id);
        var deps = dependencies.Where(d => d != key).Distinct(StringComparer.Ordinal).ToList();
        _nodes.Add(new InstallStepNode { Key = key, Step = step, DependsOn = deps });
        _frontier = [key];
        return key;
    }

    private void AddDependency(string stepId, string dependsOn) {
        for (var i = 0; i < _nodes.Count; i++) {
            var node = _nodes[i];
            if (node.Key != stepId)
                continue;

            if (!node.DependsOn.Contains(dependsOn, StringComparer.Ordinal))
                _nodes[i] = node with { DependsOn = [.. node.DependsOn, dependsOn] };
            return;
        }
    }

    private void Require(string id) {
        if (_nodes.All(n => n.Key != id))
            throw new InvalidOperationException($"Install task does not contain step '{id}'.");
    }

    private string ReserveKey(string preferred) {
        if (!string.IsNullOrWhiteSpace(preferred) && _nodes.All(n => n.Key != preferred))
            return preferred;

        return GenerateKey();
    }

    private static string GenerateKey() => $"step-{Interlocked.Increment(ref _keyCounter)}";

    private List<string> Leaves() {
        var depended = _nodes.SelectMany(n => n.DependsOn).ToHashSet(StringComparer.Ordinal);
        return [.. _nodes.Select(n => n.Key).Where(k => !depended.Contains(k))];
    }

    private void EnsureAcyclic() {
        const int visiting = 1, visited = 2;
        var marks = new Dictionary<string, int>(StringComparer.Ordinal);
        var byKey = _nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);

        foreach (var key in byKey.Keys)
            Dfs(key);

        void Dfs(string key) {
            if (marks.GetValueOrDefault(key) == visited)
                return;
            if (marks.GetValueOrDefault(key) == visiting)
                throw new InvalidOperationException($"Install task contains a dependency cycle involving '{key}'.");

            marks[key] = visiting;
            foreach (var dependency in byKey[key].DependsOn)
                Dfs(dependency);
            marks[key] = visited;
        }
    }
}

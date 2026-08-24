using System.Collections.Concurrent;

namespace Iridium.Installation;

/// <summary>
/// Executes an <see cref="InstallTask"/> DAG: schedules ready operations, runs independent
/// ones in parallel (bounded by a global operation semaphore), waits on dependencies,
/// cancels the remaining graph on the first failure, honours a CancellationToken and
/// aggregates weighted progress.
/// </summary>
public sealed class InstallTaskExecutor {
    private readonly int _maxConcurrency;

    public InstallTaskExecutor(int maxConcurrency = 4) {
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    public async Task<InstallResult> ExecuteAsync(
        InstallTask task,
        InstallContext context,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);

        var nodes = task.Nodes;
        if (nodes.Count == 0)
            return new InstallResult { Minecraft = context.Minecraft };

        var byKey = nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);
        foreach (var node in nodes)
            foreach (var dependency in node.DependsOn)
                if (!byKey.ContainsKey(dependency))
                    throw new InvalidOperationException($"Install task dependency '{dependency}' is not defined.");

        var totalWeight = nodes.Sum(n => Math.Max(0d, n.Operation.Weight));
        var completedWeight = 0d;
        var completedOperations = 0;
        var guard = new object();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var completions = nodes.ToDictionary(
            n => n.Key,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        var failures = new ConcurrentQueue<Exception>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        void Report(string key, double subProgress) {
            if (progress is null || !byKey.TryGetValue(key, out var node))
                return;

            var sub = Math.Clamp(subProgress, 0d, 1d);
            double total;
            lock (guard) {
                total = totalWeight > 0d
                    ? (completedWeight + Math.Max(0d, node.Operation.Weight) * sub) / totalWeight
                    : 0d;
            }

            progress.Report(new InstallProgress {
                CurrentOperation = node.Operation.Name,
                TotalProgress = Math.Clamp(total, 0d, 1d),
                CompletedOperations = completedOperations,
                TotalOperations = nodes.Count
            });
        }

        context.ProgressReporter = Report;

        async Task RunNodeAsync(InstallOperationNode node) {
            Exception? failure = null;
            try {
                var dependencies = node.DependsOn.Select(d => completions[d].Task).ToArray();
                await Task.WhenAll(dependencies).ConfigureAwait(false);

                await semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                try {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    lock (guard)
                        context.CurrentOperationKey = node.Key;

                    await node.Operation.ExecuteAsync(context, linkedCts.Token).ConfigureAwait(false);
                } finally {
                    semaphore.Release();
                }
            } catch (Exception ex) when (linkedCts.IsCancellationRequested) {
                failure = new OperationCanceledException("Installation canceled.", ex);
            } catch (Exception ex) {
                failure = ex;
                await linkedCts.CancelAsync().ConfigureAwait(false);
            }

            if (failure is null) {
                lock (guard) {
                    completedWeight += Math.Max(0d, node.Operation.Weight);
                    completedOperations++;
                    context.CurrentOperationKey = string.Empty;
                }

                progress?.Report(new InstallProgress {
                    CurrentOperation = string.Empty,
                    TotalProgress = totalWeight > 0d ? Math.Clamp(completedWeight / totalWeight, 0d, 1d) : 0d,
                    CompletedOperations = completedOperations,
                    TotalOperations = nodes.Count
                });
                completions[node.Key].SetResult();
            } else {
                if (failure is not OperationCanceledException)
                    failures.Enqueue(failure);
                completions[node.Key].SetException(failure);
            }
        }

        try {
            await Task.WhenAll(nodes.Select(RunNodeAsync)).ConfigureAwait(false);
        } finally {
            context.ProgressReporter = null;
            stopwatch.Stop();
        }

        return new InstallResult {
            Minecraft = context.Minecraft,
            Failures = failures.ToArray(),
            Elapsed = stopwatch.Elapsed
        };
    }
}

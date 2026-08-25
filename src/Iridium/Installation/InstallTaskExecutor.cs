using System.Collections.Concurrent;
using Iridium.Enums;

namespace Iridium.Installation;

/// <summary>
/// Executes an <see cref="InstallTask"/> DAG: schedules ready steps, runs independent ones
/// in parallel (bounded by an internal fixed step concurrency), waits on dependencies,
/// cancels the remaining graph on the first failure, honours a CancellationToken and
/// aggregates the complete <see cref="InstallProgress"/> snapshot. The shared
/// <see cref="Default"/> instance is stateless — each execution creates its own scheduler
/// state and carries a uniform per-step download concurrency into the context.
/// </summary>
public sealed class InstallTaskExecutor {
    /// <summary>Internal fixed maximum number of install steps run in parallel.</summary>
    private const int MaxConcurrency = 4;

    /// <summary>Shared, stateless default executor.</summary>
    public static InstallTaskExecutor Default { get; } = new();

    private readonly int _maxConcurrency;

    /// <summary>Creates a custom executor with an explicit step concurrency limit.</summary>
    public InstallTaskExecutor(int maxConcurrency = MaxConcurrency) {
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    /// <summary>
    /// Executes the task. <paramref name="maxDownloadConcurrency"/> is the uniform download
    /// concurrency limit applied to every download step of this execution.
    /// </summary>
    public async Task<InstallResult> ExecuteAsync(
        InstallTask task,
        InstallContext context,
        int maxDownloadConcurrency = 32,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);

        var nodes = task.Nodes;
        if (nodes.Count == 0)
            return new InstallResult { Target = context.Target };

        task.Validate();

        var states = nodes.ToDictionary(n => n.Key, n => new StepState { Step = n.Step }, StringComparer.Ordinal);

        context.DownloadConcurrency = Math.Max(1, maxDownloadConcurrency);

        void Emit() {
            if (progress is null)
                return;

            var steps = new List<InstallStepProgress>(nodes.Count);
            long completedUnits = 0;
            long totalUnits = 0;
            var completedSteps = 0;

            foreach (var node in nodes) {
                var state = states[node.Key];
                steps.Add(new InstallStepProgress {
                    Id = node.Key,
                    Name = state.Step.Name,
                    Status = state.Status,
                    Completed = state.Completed,
                    Total = state.Total
                });

                if (state.Status == InstallStepStatus.Completed)
                    completedSteps++;
                completedUnits += state.Completed;
                totalUnits += state.Total;
            }

            progress.Report(new InstallProgress {
                Steps = steps,
                CompletedSteps = completedSteps,
                TotalSteps = nodes.Count,
                CompletedUnits = completedUnits,
                TotalUnits = totalUnits
            });
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var completions = nodes.ToDictionary(
            n => n.Key,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        var failures = new ConcurrentQueue<Exception>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        async Task RunNodeAsync(InstallStepNode node) {
            var state = states[node.Key];
            Exception? failure = null;
            try {
                var dependencies = node.DependsOn.Select(d => completions[d].Task).ToArray();
                await Task.WhenAll(dependencies).ConfigureAwait(false);

                await semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                try {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    state.Status = InstallStepStatus.Running;
                    Emit();

                    await node.Step.ExecuteAsync(
                        context,
                        new Progress<InstallStepProgress>(p => {
                            state.Completed = p.Completed;
                            state.Total = p.Total;
                            Emit();
                        }),
                        linkedCts.Token).ConfigureAwait(false);
                } finally {
                    semaphore.Release();
                }
            } catch (Exception ex) when (linkedCts.IsCancellationRequested) {
                failure = new OperationCanceledException("Installation canceled.", ex);
                state.Status = InstallStepStatus.Cancelled;
            } catch (Exception ex) {
                failure = ex;
                state.Status = InstallStepStatus.Failed;
                await linkedCts.CancelAsync().ConfigureAwait(false);
            }

            if (failure is null) {
                if (state.Total == 0) {
                    state.Completed = 1;
                    state.Total = 1;
                } else {
                    state.Completed = state.Total;
                }
                state.Status = InstallStepStatus.Completed;
                Emit();
                completions[node.Key].SetResult();
            } else {
                if (failure is not OperationCanceledException)
                    failures.Enqueue(failure);
                Emit();
                completions[node.Key].SetException(failure);
            }
        }

        try {
            await Task.WhenAll(nodes.Select(RunNodeAsync)).ConfigureAwait(false);
        } finally {
            stopwatch.Stop();
        }

        return new InstallResult {
            Target = context.Target,
            Failures = [.. failures],
            Elapsed = stopwatch.Elapsed
        };
    }

    private sealed class StepState {
        public required IInstallStep Step { get; init; }
        public InstallStepStatus Status { get; set; } = InstallStepStatus.Pending;
        public long Completed { get; set; }
        public long Total { get; set; }
    }
}

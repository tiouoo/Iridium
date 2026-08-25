using Iridium.Installation.Tasks;
using Iridium.Models.Installation;

namespace Iridium.Installation.Installer;

/// <summary>
/// Base of every installer. It only abstracts the installation lifecycle — defining a task and
/// running it — and carries no business input, so installers with completely different inputs
/// (Minecraft, Java, modpacks, ...) can share it. A derived installer either overrides
/// <see cref="CreateTask()"/> and uses the inherited <see cref="InstallAsync"/>, or overrides
/// the entry with its own input and returns its own richer result.
/// </summary>
public abstract class InstallerBase {
    public event EventHandler<InstallerCompletedEventArgs>? Completed;
    public event EventHandler<InstallProgressChangedEventArgs>? ProgressChanged;

    public async Task<InstallResult> InstallAsync(
        Action<InstallProgress>? reportProgress = null,
        CancellationToken ct = default) {
        var result = await CreateTask().InstallAsync(Forward(reportProgress), ct).ConfigureAwait(false);
        ReportCompleted(result.IsSuccess, result.Failures.FirstOrDefault());
        return result;
    }

    protected abstract InstallTask CreateTask();

    protected async Task<InstallResult> RunTaskAsync(
        InstallTask task,
        Action<InstallProgress>? reportProgress,
        CancellationToken ct) {
        var result = await task.InstallAsync(Forward(reportProgress), ct).ConfigureAwait(false);
        ReportCompleted(result.IsSuccess, result.Failures.FirstOrDefault());
        return result;
    }

    private Action<InstallProgress> Forward(Action<InstallProgress>? external) =>
        p => {
            external?.Invoke(p);
            ReportProgress(p);
        };

    protected virtual void ReportProgress(InstallProgress progress) =>
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs(progress));

    protected virtual void ReportCompleted(bool isSuccess, Exception? exception) =>
        Completed?.Invoke(this, new InstallerCompletedEventArgs(isSuccess, exception));
}
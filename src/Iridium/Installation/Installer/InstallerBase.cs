using Iridium.Installation.Tasks;
using Iridium.Models.Installation;

namespace Iridium.Installation.Installer;

public abstract class InstallerBase {
    public event EventHandler<InstallerCompletedEventArgs>? Completed;
    public event EventHandler<InstallProgressChangedEventArgs>? ProgressChanged;
    
    protected virtual void ReportProgress(InstallProgress progress) =>
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs(progress));

    protected virtual void ReportCompleted(bool isSuccess, Exception? exception) =>
        Completed?.Invoke(this, new InstallerCompletedEventArgs(isSuccess, exception));
}
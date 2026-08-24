namespace Iridium.Installation.Models;

public sealed class InstallProgressChangedEventArgs : EventArgs {
    public InstallProgress Progress { get; }

    internal InstallProgressChangedEventArgs(InstallProgress progress) {
        Progress = progress;
    }
}

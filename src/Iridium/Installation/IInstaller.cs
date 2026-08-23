using Iridium.Installation.Models;

namespace Iridium.Installation;

public interface IInstaller {
    event EventHandler<InstallerCompletedEventArgs>? Completed;
    event EventHandler<InstallProgressChangedEventArgs>? ProgressChanged;
    
    Task<MinecraftInstallResult> InstallAsync(CancellationToken cancellationToken = default);
}

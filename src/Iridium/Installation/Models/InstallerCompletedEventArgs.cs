namespace Iridium.Installation.Models;

public sealed class InstallerCompletedEventArgs : EventArgs {
    public bool IsSuccess { get; }

    public Exception? Exception { get; }

    internal InstallerCompletedEventArgs(bool isSuccess, Exception? exception) {
        IsSuccess = isSuccess;
        Exception = exception;
    }
}

namespace Iridium.Installation.Tasks;

/// <summary>
/// Result of executing an <see cref="InstallTask"/>. Success/failure, elapsed time and the
/// shared <see cref="InstallState"/> (which the executed steps populated) — no business type.
/// </summary>
public sealed record InstallResult {
    public required InstallState State { get; init; }

    public TimeSpan Elapsed { get; init; }

    public IReadOnlyList<Exception> Failures { get; init; } = [];

    public bool IsSuccess => Failures.Count == 0;
}
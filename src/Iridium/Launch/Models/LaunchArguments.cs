namespace Iridium.Launch.Models;

public sealed record LaunchArguments {
    public string MainClass { get; init; } = string.Empty;

    public IReadOnlyList<string> JvmArguments { get; init; } = [];
    public IReadOnlyList<string> GameArguments { get; init; } = [];
    public IReadOnlyList<string> Natives { get; init; } = [];
}

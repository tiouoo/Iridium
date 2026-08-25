using Iridium.Enums;

namespace Iridium.Resources;


public interface IResourceDependency {
    string? ProjectId { get; }
    string? VersionId { get; }
    string? FileName { get; }
    DependencyType Type { get; }
}

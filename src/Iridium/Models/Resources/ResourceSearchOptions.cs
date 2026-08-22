using Iridium.Enums.Resources;

namespace Iridium.Models.Resources;


public sealed class ResourceSearchOptions {
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 40;
    
    public string? Query { get; init; }
    public string? GameVersion { get; init; }
    
    public ResourceSource Source { get; init; } = ResourceSource.All;
    public ResourceType Type { get; init; } = ResourceType.Mod;
    public ResourceLoaderType Loader { get; init; } = ResourceLoaderType.Any;
    public ResourceSort Sort { get; init; } = ResourceSort.Relevance;
    public SortOrder SortOrder { get; init; } = SortOrder.Desc;
    
    public IReadOnlyList<ResourceCategory> Tags { get; init; } = [];
}

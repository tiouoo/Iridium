using Iridium.Download;
using Iridium.Models.Modrinth;
using Iridium.Models.Resources;

namespace Iridium.Interfaces.Resources;

public interface IResourceClient : IDisposable {
    ResourceApiSource ResourceApiSource { get; set; }

    Task<ModrinthSearchResult?> SearchAsync(ResourceSearchOptions options, CancellationToken cancellationToken = default);
}

// public interface IResourceClient {
//
//     ResourceSource Source { get; }
//
//
//     ResourceApiOptions Options { get; }
//
//
//     Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default);
//
//
//     Task<IReadOnlyList<ResourceCategory>> GetCategoriesAsync(ResourceType type, CancellationToken cancellationToken = default);
// }

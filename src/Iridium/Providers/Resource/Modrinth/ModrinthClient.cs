using Flurl;
using Iridium.Download;
using Iridium.Enums.Resources;
using Iridium.Interfaces.Resources;

namespace Iridium.Providers.Resource.Modrinth;

public sealed partial class ModrinthClient : IResourceClient {
    public ResourceApiSource ResourceApiSource { get; }
    
    private Url BaseUrl => ResourceApiSource.GetApi(ResourceApiType.Modrinth);

    public ModrinthClient(ResourceApiSource? source = null) {
        ResourceApiSource = source ?? ResourceApiSource.Official;
    }
}

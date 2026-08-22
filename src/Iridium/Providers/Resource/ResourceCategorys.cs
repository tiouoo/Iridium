using Iridium.Enums.Resources;
using Iridium.Models.Resources;

namespace Iridium.Providers.Resource;

public static class ResourceCategories {
    public static readonly ResourceCategory WorldGen = new() {
        CurseForgeId = 406,
        Name = "worldgen",
        ModrinthSlug = "worldgen",
        DisplayName = "WorldGen",
        Type = ResourceType.Mod
    };
}
using Iridium.Enums.Resources;

namespace Iridium.Interfaces.Resources;

public interface IResourceMirror {

    string Name { get; }

    ResourceSource? GetSource(string url);

    string? TryRewrite(string url);
}

using Iridium.Interfaces.Resources;

namespace Iridium.Helpers.Resources;

/// <summary>
/// 临时资源文件镜像源「天跑」(telepao)，用于在 MCIM Files 恢复前加速 Modrinth / CurseForge 的文件下载。
///
/// 覆盖的官方 host：
/// <list type="bullet">
/// <item><c>edge.forgecdn.net/files/*</c> → <c>mod.telepao.com/files/*</c></item>
/// <item><c>cdn.modrinth.com/data/*</c> → <c>mod.telepao.com/data/*</c></item>
/// <item><c>media.forgecdn.net/*</c> → <c>mod.telepao.com/media/*</c></item>
/// </list>
/// </summary>
public sealed class TianpaoResourceMirror : IResourceMirror {
    public string Name => "Tianpao";

    private static readonly (string From, string To)[] Rewrites =
    [
        ("https://edge.forgecdn.net/files/", "https://mod.telepao.com/files/"),
        ("http://edge.forgecdn.net/files/", "https://mod.telepao.com/files/"),
        ("https://cdn.modrinth.com/data/", "https://mod.telepao.com/data/"),
        ("http://cdn.modrinth.com/data/", "https://mod.telepao.com/data/"),
        ("https://media.forgecdn.net/", "https://mod.telepao.com/media/"),
        ("http://media.forgecdn.net/", "https://mod.telepao.com/media/")
    ];

    public string? TryRewrite(string url) {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        foreach (var (from, to) in Rewrites)
            if (url.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                return to + url[from.Length..];

        return null;
    }
}

/*
 * ============================================================================
 * MCIM 镜像源（暂未启用）
 * ============================================================================
 *
 * MCIM (mod.mcimirror.top) 目前还没有实际的文件下载能力 —— 它只是把请求 302
 * 转发回官方源，所以这里先用注释把代码封存，不让它参与运行。等 MCIM Files
 * 恢复后，取消本文件的注释、并把它注册为活跃镜像即可。
 *
 * API 端点镜像（对应 Iridium.Download.ResourceApiSource.Mcim）：
 *   https://api.modrinth.com/v2/...   → https://mod.mcimirror.top/modrinth/v2/...
 *   https://api.curseforge.com/v1/... → https://mod.mcimirror.top/curseforge/v1/...
 */

/*
using Iridium.Interfaces.Resources;

namespace Iridium.Helpers.Resources;

/// <summary>
/// MCIM 文件镜像源。官方文件 CDN 与 MCIM 文件路径之间的映射规则在这里补充。
/// </summary>
public sealed class McimResourceMirror : IResourceMirror {
    public string Name => "Mcim";

    public string? TryRewrite(string url) {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // TODO: 补充 MCIM Files 恢复后的实际路径映射规则。
        return null;
    }
}
*/

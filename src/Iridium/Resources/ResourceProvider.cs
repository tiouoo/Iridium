using Iridium.Download;
using Iridium.Enums;
using Iridium.Models.Resources;
using Iridium.Resources.CurseForge;
using Iridium.Resources.Modrinth;

namespace Iridium.Resources;

/// <summary>
/// Aggregate search facade over both resource platforms. Queries Modrinth and
/// CurseForge in parallel and merges the results, deduplicating cross-platform
/// entries by slug / normalized title.
/// </summary>
public sealed class ResourceProvider {
    public ModrinthClient Modrinth { get; }
    public CurseForgeClient? CurseForge { get; }
    
    public ResourceProvider(ModrinthClient modrinth, CurseForgeClient curseForge) {
        ArgumentNullException.ThrowIfNull(modrinth);
        ArgumentNullException.ThrowIfNull(curseForge);
        
        Modrinth = modrinth;
        CurseForge = curseForge;
    }

    public ResourceProvider(ResourceApiSource? source = null, string? curseForgeApiKey = null) {
        Modrinth = new ModrinthClient(source);
        CurseForge = string.IsNullOrWhiteSpace(curseForgeApiKey)
            ? null
            : new CurseForgeClient(curseForgeApiKey, source);
    }

    public async Task<ResourceSearchPage<ResourceHit>> SearchAsync(ResourceSearchOptions options, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Source.HasFlag(ResourceSource.CurseForge) && CurseForge is null)
            throw new InvalidOperationException("CurseForge 未配置 API Key，无法搜索 CurseForge 资源。");

        Task<ResourceSearchPage<ResourceHit>>? modrinthTask = options.Source.HasFlag(ResourceSource.Modrinth)
            ? Modrinth.SearchAsync(options, cancellationToken)
            : null;
        
        Task<ResourceSearchPage<ResourceHit>>? curseForgeTask = options.Source.HasFlag(ResourceSource.CurseForge)
            ? CurseForge!.SearchAsync(options, cancellationToken)
            : null;

        if (modrinthTask is null && curseForgeTask is null)
            return new ResourceSearchPage<ResourceHit>([], 0, options.Page, options.PageSize);

        if (modrinthTask is not null && curseForgeTask is not null)
            await Task.WhenAll(modrinthTask, curseForgeTask);

        var hits = new List<ResourceHit>(modrinthTask is not null ? 40 : 0);
        var totalCount = 0;

        if (modrinthTask is not null) {
            var result = await modrinthTask;
            hits.AddRange(result.Items);
            totalCount += result.TotalCount;
        }

        if (curseForgeTask is not null) {
            var result = await curseForgeTask;
            hits.AddRange(result.Items);
            totalCount += result.TotalCount;
        }

        return new ResourceSearchPage<ResourceHit>(Merge(hits), totalCount, options.Page, options.PageSize);
    }

    public async Task<ResourceSearchPage<ResourceHit>> SearchAsync(Action<ResourceSearchOptionsBuilder> configure, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ResourceSearchOptionsBuilder();
        configure(builder);
        return await SearchAsync(builder.Build(), cancellationToken);
    }

    private static List<ResourceHit> Merge(List<ResourceHit> hits) {
        if (hits.Count < 2)
            return hits;

        var merged = new List<ResourceHit>(hits.Count);
        foreach (var hit in hits) {
            var index = merged.FindIndex(existing => IsSameProject(existing, hit));
            if (index < 0) {
                merged.Add(hit);
                continue;
            }

            if (hit.Downloads > merged[index].Downloads)
                merged[index] = hit;
        }

        return merged;
    }

    private static bool IsSameProject(ResourceHit left, ResourceHit right) {
        if (left.Source == right.Source)
            return false;

        if (!string.IsNullOrWhiteSpace(left.Slug) && !string.IsNullOrWhiteSpace(right.Slug))
            return string.Equals(left.Slug, right.Slug, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(left.Title) && !string.IsNullOrWhiteSpace(right.Title) &&
               string.Equals(Normalize(left.Title), Normalize(right.Title), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));
}

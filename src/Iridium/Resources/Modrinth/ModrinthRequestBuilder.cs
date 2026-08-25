using System.Text.Json;
using Iridium.Enums;
using Iridium.Extensions;
using Iridium.Resources.Models;

using ModrinthSearchResultContext = Iridium.Resources.Modrinth.ModrinthSearchResultContext;

namespace Iridium.Resources.Modrinth;

internal static class ModrinthRequestBuilder {
    public static string BuildFacets(ResourceSearchOptions options) {
        var facets = new List<List<string>>(options.Tags.Count + 3) {
            new() { $"project_type:{options.Type.ToModrinthProjectType()}" }
        };

        if (!string.IsNullOrWhiteSpace(options.GameVersion))
            facets.Add([$"versions:'{options.GameVersion}'"]);

        facets.AddRange(options.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag.ModrinthSlug))
            .Select(tag => (List<string>)[$"categories:'{tag.ModrinthSlug}'"]));

        if (options.Loader.ToModrinthLoader() is { } loader)
            facets.Add([$"categories:'{loader}'"]);

        return JsonSerializer.Serialize(facets,
            ModrinthSearchResultContext.Default.ListListString);
    }

    public static string ToAlgorithm(HashAlgorithm algorithm) => algorithm switch {
        HashAlgorithm.Sha1 => "sha1",
        HashAlgorithm.Sha512 => "sha512",
        _ => "sha1"
    };
}

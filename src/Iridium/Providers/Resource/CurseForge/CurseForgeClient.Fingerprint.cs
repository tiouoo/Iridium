using Iridium.Models.Resources.CurseForge;
using CurseForgeJsonContext = Iridium.Models.Resources.CurseForge.CurseForgeJsonContext;

namespace Iridium.Providers.Resource.CurseForge;

public partial class CurseForgeClient {
    public async Task<CurseForgeFingerprintResult> GetFilesByFingerprintsAsync(IEnumerable<uint> fingerprints, CancellationToken cancellationToken = default) {
        var values = fingerprints.Distinct().ToArray();
        if (values.Length == 0)
            return new CurseForgeFingerprintResult { Data = new CurseForgeFingerprintData() };

        var url = BaseUrl.AppendPathSegments("fingerprints", MinecraftGameId);
        
        return await PostJsonAsync(url, 
            new Models.Resources.CurseForge.CurseForgeFingerprintRequest { Fingerprints = values }, 
            CurseForgeJsonContext.Default.CurseForgeFingerprintRequest, 
            CurseForgeJsonContext.Default.CurseForgeFingerprintResult, cancellationToken) ?? new CurseForgeFingerprintResult();
    }
}

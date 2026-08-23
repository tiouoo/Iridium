namespace Iridium.Models.Download;

public sealed record DownloadResponse {
    public int SuccessCount { get; init; }
    public int FailCount { get; init; }
    public IReadOnlyList<Exception> Exceptions { get; init; } = [];
}

public sealed record DownloadRequest {
    public string Url { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;

    /// <summary>
    /// Additional candidate URLs for the same file (e.g. a mirror). Tried in order with
    /// timeout-based failover when <see cref="Iridium.Download.SourceSelector"/> is in a
    /// mirror-aware mode; ignored otherwise.
    /// </summary>
    public IReadOnlyList<string>? AlternateUrls { get; init; }

    public long Size { get; init; }
    
    public FileInfo FileInfo => new(LocalPath);
    
    public Action<EventArgs>? Completed { get; init; }
    public Action<ResourceDownloadProgressChangedEventArgs>? ProgressChanged { get; init; }
}
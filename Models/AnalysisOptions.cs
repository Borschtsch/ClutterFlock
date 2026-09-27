namespace ClutterFlock.Models;

public sealed class AnalysisOptions
{
    // Zero selects the CPU-based adaptive budget. Explicit caps remain available to service callers.
    public int MaxConcurrentReads { get; init; }
    public ClutterFlock.Core.StorageTopology? StorageTopology { get; init; }
    public bool UseContentSampling { get; init; } = true;
    public bool QualifyingFoldersOnly { get; init; }
    public bool RetainFileMatches { get; init; } = true;
    public AnalysisStatistics Statistics { get; init; } = new();
}

public sealed class AnalysisStatistics
{
    public ClutterFlock.Core.StorageScheduleSnapshot? StorageSchedule { get; internal set; }
    internal long bytes, hashes, sampled, rejected, cached, empty, pairs;
    public long BytesRead => Interlocked.Read(ref bytes);
    public long FullHashesComputed => Interlocked.Read(ref hashes);
    public long FilesSampled => Interlocked.Read(ref sampled);
    public long FilesRejectedBySample => Interlocked.Read(ref rejected);
    public long CachedHashesUsed => Interlocked.Read(ref cached);
    public long EmptyFilesVerified => Interlocked.Read(ref empty);
    public long FilePairsEmitted => Interlocked.Read(ref pairs);
}

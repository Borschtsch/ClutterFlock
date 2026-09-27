using System.IO;
using System.Security.Cryptography;
using ClutterFlock.Models;

namespace ClutterFlock.Core;

public partial class DuplicateAnalyzer
{
    private const string EmptyContentHash = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
    private void ValidateMetadata(string path)
    {
        var info = new FileInfo(path);
        var metadata = _cacheManager.GetFileMetadata(path);
        if (metadata == null || info.Length != metadata.Size || info.LastWriteTimeUtc != metadata.LastWriteTime.ToUniversalTime())
            throw new IOException("File changed after scanning; run comparison again.");
    }

    private string VerifyEmpty(string path, AnalysisStatistics stats)
    {
        try
        {
            ValidateMetadata(path);
            if (_cacheManager.GetFileMetadata(path)!.Size != 0) return "";
            _cacheManager.CacheFileHash(path, EmptyContentHash);
            Interlocked.Increment(ref stats.empty);
            return EmptyContentHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _errorRecoveryService.LogSkippedItem(path, ex.Message); return ""; }
    }

    private async Task<string> SampleAsync(string path, AnalysisStatistics stats, CancellationToken token, Action<int>? bytesRead = null, StorageWorkContext? work = null)
    {
        try
        {
            ValidateMetadata(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
            const int size = 64 * 1024;
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
            try
            {
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var offset in new[] { 0L, (stream.Length - size) / 2, stream.Length - size })
                {
                    stream.Position = offset;
                    await stream.ReadExactlyAsync(buffer.AsMemory(0, size), token).ConfigureAwait(false);
                    if (work == null) digest.AppendData(buffer, 0, size);
                    else await work.RunCpuAsync(() => digest.AppendData(buffer, 0, size), token).ConfigureAwait(false);
                    Interlocked.Add(ref stats.bytes, size); bytesRead?.Invoke(size);
                }
                ValidateMetadata(path); Interlocked.Increment(ref stats.sampled);
                var sample = "";
                if (work == null) sample = Convert.ToHexString(digest.GetHashAndReset());
                else await work.RunCpuAsync(() => sample = Convert.ToHexString(digest.GetHashAndReset()), token).ConfigureAwait(false);
                _cacheManager.GetFileMetadata(path)!.ContentSample = "v1:" + sample;
                return sample;
            }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _errorRecoveryService.LogSkippedItem(path, $"Content sample failed: {ex.Message}"); return ""; }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using ClutterFlock.Models;

namespace ClutterFlock.Core;

public partial class DuplicateAnalyzer
{
    private async Task<List<FileMatch>> CompareFileHashesAsync(Dictionary<string, List<string>> duplicateGroups,
        IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken,
        Func<IReadOnlyList<FileMatch>, CancellationToken, Task>? matchesFound, AnalysisOptions options)
    {
        var matches = options.RetainFileMatches ? new ConcurrentBag<FileMatch>() : null;
        var stats = options.Statistics;
        var qualified = new ConcurrentDictionary<(string, string), byte>();
        var folders = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Folder(string file) => folders.GetOrAdd(file, static f => Path.GetDirectoryName(f)!);
        var groups = duplicateGroups.Values.ToList();
        var emptyGroups = options.QualifyingFoldersOnly ? groups.Where(g => _cacheManager.GetFileMetadata(g[0])!.Size == 0).ToList() : new();
        if (options.QualifyingFoldersOnly) groups.RemoveAll(g => _cacheManager.GetFileMetadata(g[0])!.Size == 0);
        var totalFiles = groups.Sum(g => g.Count) + emptyGroups.Sum(g => g.Count);
        var completedFiles = 0;
        var clock = Stopwatch.StartNew();
        var topology = options.StorageTopology ?? StorageTopology.Discover(_cacheManager.GetAllFolderInfo().Keys);
        var budget = options.MaxConcurrentReads;
        var scheduler = new AdaptiveStorageScheduler(topology.Devices, budget, availableProcessors: topology.AvailableProcessors);
        void Report() => progress?.Report(new AnalysisProgress
        {
            Phase = AnalysisPhase.ComparingFiles, CurrentProgress = Volatile.Read(ref completedFiles), MaxProgress = totalFiles,
            StatusMessage = $"Verifying contents: {Volatile.Read(ref completedFiles):N0}/{totalFiles:N0} files · {stats.BytesRead / 1048576.0 / Math.Max(.001, clock.Elapsed.TotalSeconds):N1} MiB/s"
        });
        using var reportingStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        async Task ReportPeriodically()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            try { while (await timer.WaitForNextTickAsync(reportingStop.Token).ConfigureAwait(false)) Report(); }
            catch (OperationCanceledException) when (reportingStop.IsCancellationRequested) { }
        }
        var reporting = progress == null ? Task.CompletedTask : ReportPeriodically();
        async Task Publish(List<FileMatch> batch, CancellationToken token)
        {
            if (batch.Count == 0) return;
            foreach (var match in batch) matches?.Add(match);
            Interlocked.Add(ref stats.pairs, batch.Count);
            if (matchesFound != null) await matchesFound(batch.ToArray(), token).ConfigureAwait(false);
            batch.Clear();
        }
        void QueueVerification(IEnumerable<string> files)
        {
            var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var gate = new object();
            foreach (var file in files) scheduler.Enqueue(topology.Resolve(file), async (work, fileToken) =>
            {
                var hash = _cacheManager.GetFileHash(file);
                if (hash != null) Interlocked.Increment(ref stats.cached);
                else if (_cacheManager.GetFileMetadata(file)!.Size == 0) { hash = VerifyEmpty(file, stats); work.Report(1); }
                else
                {
                    hash = await ComputeFileHashAsync(file, fileToken, count =>
                    { Interlocked.Add(ref stats.bytes, count); work.Report(count); }, work).ConfigureAwait(false);
                    if (hash.Length > 0) { _cacheManager.CacheFileHash(file, hash); Interlocked.Increment(ref stats.hashes); }
                }
                Interlocked.Increment(ref completedFiles);
                if (hash.Length == 0) return;
                string[] prior = [];
                await work.RunCpuAsync(() =>
                {
                    lock (gate)
                    {
                        if (!byHash.TryGetValue(hash, out var bucket)) byHash[hash] = bucket = new();
                        prior = bucket.ToArray(); bucket.Add(file);
                    }
                }, fileToken).ConfigureAwait(false);
                var batch = new List<FileMatch>(256);
                for (var offset = 0; offset < prior.Length; offset += 256)
                {
                    await work.RunCpuAsync(() =>
                    {
                        for (var i = offset; i < Math.Min(prior.Length, offset + 256); i++)
                        {
                            fileToken.ThrowIfCancellationRequested();
                            var other = prior[i];
                            if (Folder(file).Equals(Folder(other), StringComparison.OrdinalIgnoreCase)) continue;
                            var match = string.Compare(file, other, StringComparison.OrdinalIgnoreCase) <= 0 ? new FileMatch(file, other) : new FileMatch(other, file);
                            if (options.QualifyingFoldersOnly) qualified.TryAdd((Folder(match.PathA), Folder(match.PathB)), 0);
                            batch.Add(match);
                        }
                    }, fileToken).ConfigureAwait(false);
                    await Publish(batch, fileToken).ConfigureAwait(false);
                }
            });
        }
        try
        {
            Report();
            foreach (var files in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Samples only reject candidates. Equal samples still require a full SHA-256.
                if (options.UseContentSampling && _cacheManager.GetFileMetadata(files[0])!.Size >= 1024 * 1024
                    && files.All(f => _cacheManager.GetFileHash(f) == null))
                {
                    var sampled = new ConcurrentDictionary<string, ConcurrentBag<string>>();
                    var remaining = files.Count;
                    foreach (var file in files) scheduler.Enqueue(topology.Resolve(file), async (work, fileToken) =>
                    {
                        var sample = await SampleAsync(file, stats, fileToken, count => work.Report(count), work).ConfigureAwait(false);
                        if (sample.Length == 0) Interlocked.Increment(ref completedFiles);
                        else sampled.GetOrAdd(sample, _ => new()).Add(file);
                        if (Interlocked.Decrement(ref remaining) != 0) return;
                        foreach (var bucket in sampled.Values)
                        {
                            fileToken.ThrowIfCancellationRequested();
                            if (bucket.Count == 1) { Interlocked.Increment(ref stats.rejected); Interlocked.Increment(ref completedFiles); }
                            else QueueVerification(bucket);
                        }
                    });
                }
                else QueueVerification(files);
            }
            await scheduler.RunAsync(cancellationToken, snapshot => stats.StorageSchedule = snapshot).ConfigureAwait(false);

            // Join empty-file names only for folder edges already qualified by shared content.
            // This avoids materializing N*(N-1)/2 empty-only pairs, without losing empty evidence.
            if (options.QualifyingFoldersOnly && !qualified.IsEmpty && emptyGroups.Count > 0)
            {
                var emptyByFolder = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var emptyScheduler = new AdaptiveStorageScheduler(topology.Devices, budget, availableProcessors: topology.AvailableProcessors);
                foreach (var file in emptyGroups.SelectMany(g => g))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    emptyScheduler.Enqueue(topology.Resolve(file), (work, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (VerifyEmpty(file, stats).Length > 0)
                            emptyByFolder.GetOrAdd(Folder(file), _ => new(StringComparer.OrdinalIgnoreCase))[Path.GetFileName(file)] = file;
                        work.Report(1);
                        return Task.CompletedTask;
                    });
                }
                await emptyScheduler.RunAsync(cancellationToken, snapshot => stats.StorageSchedule = snapshot).ConfigureAwait(false);
                var batch = new List<FileMatch>(256);
                foreach (var (left, right) in qualified.Keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!emptyByFolder.TryGetValue(left, out var a) || !emptyByFolder.TryGetValue(right, out var b)) continue;
                    var smaller = a.Count <= b.Count ? a : b;
                    var larger = ReferenceEquals(smaller, a) ? b : a;
                    foreach (var (name, file) in smaller)
                    {
                        if (!larger.TryGetValue(name, out var other)) continue;
                        batch.Add(ReferenceEquals(smaller, a) ? new(file, other) : new(other, file));
                        if (batch.Count == 256) await Publish(batch, cancellationToken).ConfigureAwait(false);
                    }
                }
                await Publish(batch, cancellationToken).ConfigureAwait(false);
            }
            Interlocked.Add(ref completedFiles, emptyGroups.Sum(g => g.Count));
            Report(); return matches?.ToList() ?? new List<FileMatch>();
        }
        finally { reportingStop.Cancel(); await reporting.ConfigureAwait(false); }
    }
}

// Historical scheduling notes retained; the configurable global read budget replaces volume gates.
        // Separate drive letters/UNC shares have independent queues. Two readers per
        // volume limits seeking on HDDs; four globally bounds memory and outstanding I/O.
        // Keep counts current even while all readers are waiting on slow/locked files.
                // A single group can contain many copies. Read its files concurrently as
                // well, under the same global/volume limits, and emit matches per file.

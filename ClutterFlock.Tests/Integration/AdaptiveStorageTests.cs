using System.Collections.Concurrent;
using ClutterFlock.Core;
using ClutterFlock.Models;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public sealed class AdaptiveStorageTests
{
    private string _root = null!;
    private string _file = null!;
    public TestContext TestContext { get; set; } = null!;
    [TestInitialize]
    public void Setup()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "ClutterFlockAdaptive", Guid.NewGuid().ToString("N")));
        _file = Path.Combine(_root, "payload"); File.WriteAllBytes(_file, new byte[65536]);
    }
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockAdaptive")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }

    [TestMethod]
    [DataRow(1, 3, 1)]
    [DataRow(2, 3, 1)]
    [DataRow(4, 3, 3)]
    [DataRow(16, 15, 15)]
    public async Task DriveWorkers_KeepIndependentIoWhileCpuSectionsRespectUiReserve(int availableCpus, int expectedIoBudget, int expectedCpuBudget)
    {
        var devices = Enumerable.Range(0, 3).Select(i => new StorageDevice($"device-{i}", $"Device {i}", true)).ToArray();
        var scheduler = new AdaptiveStorageScheduler(devices, processorBudget: 32, availableProcessors: availableCpus);
        var seen = new ConcurrentDictionary<string, byte>();
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cpuActive = 0; var cpuPeak = 0; var ioActive = 0; var ioPeak = 0; var completed = 0;
        var gate = new object();
        foreach (var device in devices)
            for (var i = 0; i < (device == devices[0] ? 80 : 1); i++)
                scheduler.Enqueue(device, async (work, token) =>
                {
                    lock (gate) { ioActive++; ioPeak = Math.Max(ioPeak, ioActive); }
                    try
                    {
                        seen.TryAdd(device.Id, 0);
                        if (seen.Count == 3) allStarted.TrySetResult();
                        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                        var data = await File.ReadAllBytesAsync(_file, token);
                        work.Report(data.Length);
                        await work.RunCpuAsync(() =>
                        {
                            lock (gate) { cpuActive++; cpuPeak = Math.Max(cpuPeak, cpuActive); }
                            try
                            {
                                for (var hash = 0; hash < 20; hash++) System.Security.Cryptography.SHA256.HashData(data);
                                Interlocked.Increment(ref completed);
                            }
                            finally { lock (gate) cpuActive--; }
                        }, token);
                    }
                    finally { lock (gate) ioActive--; }
                });
        // The original device-minimum contract was:
        // Every device needs an opportunity to start, even when devices exceed the CPU budget.
        // The UI reserve now takes precedence, so queued devices receive fair turns instead.
        // These historical global-cap notes are superseded: each drive starts I/O independently,
        // and only computation waits for the shared CPU limit.
        await scheduler.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(expectedIoBudget, scheduler.Budget); Assert.AreEqual(expectedCpuBudget, scheduler.CpuBudget);
        Assert.IsTrue(ioPeak >= 3 && ioPeak <= expectedIoBudget);
        Assert.IsTrue(cpuPeak >= 1 && cpuPeak <= expectedCpuBudget);
        Assert.AreEqual(0, ioActive); Assert.AreEqual(0, cpuActive); Assert.AreEqual(82, completed);
        Assert.AreEqual(3, scheduler.Snapshot.MinimumWorkers);
    }

    [TestMethod]
    public async Task FourDeviceQueues_KeepFourAllocatedSubworkers_WhenOperationsBecomeIdle()
    {
        var devices = Enumerable.Range(0, 4).Select(i => new StorageDevice($"device-{i}", $"Device {i}", true)).ToArray();
        var scheduler = new AdaptiveStorageScheduler(devices, availableProcessors: 2,
            sampleInterval: TimeSpan.FromMilliseconds(30));
        var releases = devices.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var allRunning = new TaskCompletionSource<StorageScheduleSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oneRunning = new TaskCompletionSource<StorageScheduleSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Four independent public device queues performing real reads, with controlled completion.
        for (var i = 0; i < devices.Length; i++)
        {
            var index = i;
            scheduler.Enqueue(devices[i], async (work, token) =>
            {
                work.Report((await File.ReadAllBytesAsync(_file, token)).Length);
                Interlocked.Increment(ref reads);
                await releases[index].Task.WaitAsync(token);
            });
        }
        var run = scheduler.RunAsync(stop.Token, snapshot =>
        {
            if (snapshot.RunningOperations == 4) allRunning.TrySetResult(snapshot);
            if (snapshot.RunningOperations == 1) oneRunning.TrySetResult(snapshot);
        });
        try
        {
            var busy = await allRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(4, busy.AllocatedSubworkers);
            foreach (var release in releases.Take(3)) release.TrySetResult();
            var partlyIdle = await oneRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(4, partlyIdle.MinimumWorkers);
            Assert.AreEqual(4, partlyIdle.AllocatedSubworkers);
            Assert.IsTrue(partlyIdle.Devices.All(d => d.Workers >= 1));
            StringAssert.Contains(partlyIdle.Summary, "4/4 I/O subworkers allocated");
            StringAssert.Contains(partlyIdle.Summary, "1 operations running");
        }
        finally
        {
            foreach (var release in releases) release.TrySetResult();
            await run;
        }
        Assert.AreEqual(4, reads);
        Assert.AreEqual(4, scheduler.Snapshot.AllocatedSubworkers);
        Assert.AreEqual(0, scheduler.Snapshot.RunningOperations);
        StringAssert.Contains(scheduler.Snapshot.Summary, "4/4 I/O subworkers allocated");
    }

    [TestMethod]
    public async Task MultipleStorageQueues_FullHashAndSamplingShareCpuCapWithoutLosingEvidence()
    {
        var roots = Enumerable.Range(0, 3).Select(i => Directory.CreateDirectory(Path.Combine(_root, $"drive-{i}")).FullName).ToArray();
        var topology = new StorageTopology(roots.Select((path, i) => new StoragePath(path, new($"storage-{i}", $"Storage {i}", true))));
        foreach (var root in roots) File.WriteAllBytes(Path.Combine(root, "shared.bin"), new byte[2 * 1024 * 1024]);
        var cache = new CacheManager(); var errors = new ErrorRecoveryService();
        var folders = await new FolderScanner(cache, errors).ScanFoldersAsync(roots, null, CancellationToken.None, topology);
        var options = new AnalysisOptions { StorageTopology = topology, MaxConcurrentReads = 1 };
        var matches = await new DuplicateAnalyzer(cache, errors).FindDuplicateFilesAsync(folders, null, CancellationToken.None, options: options);
        Assert.HasCount(3, matches); Assert.AreEqual(3L, options.Statistics.FilesSampled); Assert.AreEqual(3L, options.Statistics.FullHashesComputed);
        Assert.AreEqual(3, options.Statistics.StorageSchedule!.Budget);
        Assert.AreEqual(1, options.Statistics.StorageSchedule.CpuBudget);
        Assert.IsFalse(errors.GetErrorSummary().HasErrors);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task SubsequentRun_RefreshesWindowsCpuAffinity_AndKeepsItsCapturedBudget()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var originalAffinity = process.ProcessorAffinity;
        var bits = unchecked((ulong)originalAffinity.ToInt64());
        if (System.Numerics.BitOperations.PopCount(bits) < 2) Assert.Inconclusive("Requires two allowed logical processors for an affinity-change integration test.");
        var firstBit = bits & unchecked(0UL - bits);
        var rest = bits & ~firstBit;
        var secondBit = rest & unchecked(0UL - rest);
        try
        {
            process.ProcessorAffinity = new IntPtr(unchecked((long)(firstBit | secondBit)));
            var firstRun = StorageTopology.Discover([_root]);
            process.ProcessorAffinity = new IntPtr(unchecked((long)firstBit));
            var nextRun = StorageTopology.Discover([_root]);
            Assert.AreEqual(2, firstRun.AvailableProcessors);
            Assert.AreEqual(1, nextRun.AvailableProcessors);
            foreach (var run in new[] { firstRun, nextRun })
            {
                var scheduler = new AdaptiveStorageScheduler(run.Devices, availableProcessors: run.AvailableProcessors);
                scheduler.Enqueue(run.Resolve(_file), async (work, token) =>
                    work.Report((await File.ReadAllBytesAsync(_file, token)).Length));
                await scheduler.RunAsync(CancellationToken.None);
                Assert.AreEqual(1, scheduler.Budget);
                Assert.AreEqual(run.AvailableProcessors, scheduler.AvailableProcessors);
            }
        }
        finally { process.ProcessorAffinity = originalAffinity; }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task MovingWindows_GrowFastDevice_BackOffSerializedDevice_AndRespectBudget()
    {
        var fast = new StorageDevice("fast", "Fast", true);
        var serialized = new StorageDevice("serialized", "Serialized", true);
        var scheduler = new AdaptiveStorageScheduler([fast, serialized], 6, TimeSpan.FromMilliseconds(75), 4, availableProcessors: 7);
        var snapshots = new List<StorageScheduleSnapshot>();
        using var serialStorage = new SemaphoreSlim(1);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serialContenders = 0;
        // Component boundary: real reads with controlled storage latency, no mocked services.
        // Give competing requests a clear saturation penalty. A flat 35 ms service time
        // can appear to improve by 5% from timer quantization alone in a 300 ms window.
        // Count actual outstanding requests, independently of scheduler allocations.
        for (var i = 0; i < 320; i++) scheduler.Enqueue(fast, async (work, token) =>
        {
            await Task.Delay(25, token);
            work.Report((await File.ReadAllBytesAsync(_file, token)).Length);
        });
        for (var i = 0; i < 160; i++) scheduler.Enqueue(serialized, async (work, token) =>
        {
            Interlocked.Increment(ref serialContenders);
            try
            {
                await serialStorage.WaitAsync(token);
                try
                {
                    await Task.Delay(Volatile.Read(ref serialContenders) > 1 ? 140 : 35, token);
                    var data = await File.ReadAllBytesAsync(_file, token);
                    work.Report(data.Length / 8);
                }
                finally { serialStorage.Release(); }
            }
            finally { Interlocked.Decrement(ref serialContenders); }
        });
        await scheduler.RunAsync(stop.Token, snapshots.Add);
        var trace = string.Join(Environment.NewLine, snapshots.Select((s, i) =>
            $"Window {i}: " + string.Join("; ", s.Devices.Select(d =>
                $"{d.Device}: allocated={d.Workers}, active={d.Active}, queued={d.Queued}, rate={d.UnitsPerSecond:F1}"))));
        var fastPeak = snapshots.Max(s => s.Devices.Single(d => d.Device == "Fast").Workers);
        Assert.IsGreaterThan(1, fastPeak, "The fast device must grow.\n" + trace);
        var slow = snapshots.Select(s => s.Devices.Single(d => d.Device == "Serialized")).ToList();
        var trial = slow.FindIndex(s => s.Workers > 1);
        Assert.IsGreaterThanOrEqualTo(0, trial, "Every device needs an opportunity to probe additional workers.\n" + trace);
        Assert.IsTrue(slow.Skip(trial + 1).Any(s => s.Workers == 1 && s.Queued > 0), "No throughput gain must reduce concurrency while work remains.\n" + trace);
        Assert.IsTrue(snapshots.All(s => s.Devices.Sum(d => d.Active) <= 6 && s.Devices.Sum(d => d.Workers) <= 6),
            "Running and allocated subworkers must stay within the shared budget.\n" + trace);
        Assert.IsTrue(snapshots.All(s => s.Devices.All(d => d.Workers >= 1)),
            "Each storage device must retain at least one subworker.\n" + trace);
        Assert.AreEqual(0, serialContenders, "Every serialized request must drain before the test returns.");
        TestContext.WriteLine($"Fast-device peak: {fastPeak} workers; serialized device returned to one; {snapshots.Count} measured windows.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Rebalancing_RejectsNetLoss_AndRevisitsDevicesWhenThroughputChanges(bool changeThroughput)
    {
        var a = new StorageDevice("a", "A", true);
        var b = new StorageDevice("b", "B", true);
        var scheduler = new AdaptiveStorageScheduler([a, b], 3, TimeSpan.FromMilliseconds(75), 4, availableProcessors: 4);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var snapshots = new List<(double Time, StorageScheduleSnapshot State)>();
        using var saturatedA = new SemaphoreSlim(1);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(changeThroughput ? 8 : 4));
        // Real asynchronous reads through the public scheduler boundary. Controlled latency
        // changes which device benefits from the single spare slot during the same run.
        foreach (var device in new[] { a, b })
            for (var i = 0; i < 2000; i++) scheduler.Enqueue(device, async (work, token) =>
            {
                var changed = changeThroughput && clock.Elapsed.TotalSeconds >= 4;
                var serialized = changed && device == a;
                if (serialized) await saturatedA.WaitAsync(token);
                try
                {
                    await Task.Delay(changed ? (device == a ? 70 : 5) : (device == a ? 10 : 80), token);
                    work.Report((await File.ReadAllBytesAsync(_file, token)).Length);
                }
                finally { if (serialized) saturatedA.Release(); }
            });
        await Assert.ThrowsAsync<OperationCanceledException>(() => scheduler.RunAsync(stop.Token,
            state => snapshots.Add((clock.Elapsed.TotalSeconds, state))));
        var before = snapshots.Where(s => s.Time < 4).Select(s => s.State).ToList();
        var firstGrowth = before.FindIndex(s => s.Devices.Single(d => d.Device == "A").Workers == 2);
        Assert.IsGreaterThanOrEqualTo(0, firstGrowth);
        var transfer = before.FindIndex(firstGrowth + 1, s => s.Devices.Single(d => d.Device == "B").Workers == 2);
        Assert.IsGreaterThan(firstGrowth, transfer, "The controller must probe another device even when all slots are assigned.");
        Assert.IsTrue(before.Skip(transfer + 1).Any(s => s.Devices.Single(d => d.Device == "A").Workers == 2),
            "A gain on the receiver must be rejected when the donor loses more throughput.");
        if (changeThroughput)
            Assert.IsTrue(snapshots.Count(s => s.Time > 6 &&
                s.State.Devices.Single(d => d.Device == "B").Workers == 2 &&
                s.State.Devices.Single(d => d.Device == "B").UnitsPerSecond >
                s.State.Devices.Single(d => d.Device == "A").UnitsPerSecond * 2) >= 4,
                "Capacity must migrate to the newly faster device after saturation, for multiple measured windows.");
        Assert.IsTrue(snapshots.All(s => s.State.Devices.All(d => d.Workers >= 1) &&
            s.State.Devices.Sum(d => d.Workers) <= 3 && s.State.Devices.Sum(d => d.Active) <= 3));
    }

    [TestMethod]
    public async Task BackloggedDevice_GrowsBeyondSmallAllocation_AndProcessesEveryJobExactlyOnce()
    {
        var device = new StorageDevice("fast", "Fast", true);
        var scheduler = new AdaptiveStorageScheduler([device], availableProcessors: 16,
            sampleInterval: TimeSpan.FromMilliseconds(50));
        var snapshots = new List<StorageScheduleSnapshot>();
        var processed = new ConcurrentDictionary<int, int>();
        for (var i = 0; i < 1500; i++)
        {
            var job = i;
            scheduler.Enqueue(device, async (work, token) =>
            {
                await Task.Delay(10, token);
                work.Report((await File.ReadAllBytesAsync(_file, token)).Length);
                processed.AddOrUpdate(job, 1, (_, count) => count + 1);
            });
        }
        await scheduler.RunAsync(CancellationToken.None, snapshots.Add).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.HasCount(1500, processed);
        Assert.IsTrue(processed.Values.All(count => count == 1));
        Assert.IsTrue(snapshots.Any(s => s.AllocatedSubworkers >= 8 && s.RunningOperations >= 8),
            "A sustained backlog with improving throughput must grow beyond one or a few subworkers.");
        Assert.IsTrue(snapshots.All(s => s.AllocatedSubworkers is >= 1 and <= 15 && s.RunningOperations <= 15));
    }

    [TestMethod]
    public async Task DynamicQueueAndCancellation_DrainWorkersBeforeReturning()
    {
        var device = new StorageDevice("one", "One", true);
        var scheduler = new AdaptiveStorageScheduler([device], 2);
        var completed = 0;
        scheduler.Enqueue(device, (work, token) =>
        {
            for (var i = 0; i < 20; i++) scheduler.Enqueue(device, async (childWork, childToken) =>
            { childWork.Report((await File.ReadAllBytesAsync(_file, childToken)).Length); Interlocked.Increment(ref completed); });
            return Task.CompletedTask;
        });
        await scheduler.RunAsync(CancellationToken.None); Assert.AreEqual(20, completed);
        using var cancel = new CancellationTokenSource();
        scheduler = new AdaptiveStorageScheduler([device], 2);
        var active = 0;
        scheduler.Enqueue(device, async (_, token) =>
        {
            Interlocked.Increment(ref active);
            try { cancel.Cancel(); await Task.Delay(5000, token); }
            finally { Interlocked.Decrement(ref active); }
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => scheduler.RunAsync(cancel.Token));
        Assert.AreEqual(0, active);
    }

    [TestMethod]
    public async Task MultipleRoots_SharedDeviceScanAndHash_PreserveAllEvidenceAndCachedShortcuts()
    {
        var a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        File.WriteAllText(Path.Combine(a, "same"), "same"); File.WriteAllText(Path.Combine(b, "same"), "same");
        File.WriteAllText(Path.Combine(a, "empty"), ""); File.WriteAllText(Path.Combine(b, "empty"), "");
        File.WriteAllText(Path.Combine(a, "unique"), "unique");
        var topology = StorageTopology.Discover([_root, a, b]);
        Assert.AreEqual(topology.Resolve(a).Id, topology.Resolve(b).Id);
        var cache = new CacheManager(); var errors = new ErrorRecoveryService();
        var scanner = new FolderScanner(cache, errors);
        var folders = await scanner.ScanFoldersAsync([a, b, a], null, CancellationToken.None, topology);
        Assert.HasCount(2, folders); Assert.HasCount(2, cache.GetAllFolderInfo());
        var analyzer = new DuplicateAnalyzer(cache, errors);
        var options = new AnalysisOptions { StorageTopology = topology, QualifyingFoldersOnly = true };
        var matches = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None, options: options);
        Assert.HasCount(2, matches); Assert.AreEqual(2L, options.Statistics.FullHashesComputed);
        Assert.HasCount(topology.Devices.Count, options.Statistics.StorageSchedule!.Devices);
        var cached = new AnalysisOptions { StorageTopology = topology, QualifyingFoldersOnly = true };
        var again = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None, options: cached);
        CollectionAssert.AreEquivalent(matches, again); Assert.AreEqual(0L, cached.Statistics.BytesRead);
        TestContext.WriteLine($"OS topology: {string.Join(", ", topology.Devices.Select(d => $"{d.Label} (physical={d.PhysicalIdentityKnown})"))}");
    }
}

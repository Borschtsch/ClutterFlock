using System.Collections.Concurrent;
using System.Diagnostics;

namespace ClutterFlock.Core;

public sealed record StorageWorkerSnapshot(string Device, int Workers, int Active, int Queued, double UnitsPerSecond, bool PhysicalIdentityKnown);
public sealed record StorageScheduleSnapshot(int Budget, IReadOnlyList<StorageWorkerSnapshot> Devices)
{
    public int CpuBudget { get; init; }
    // One controller per drive, plus the existing UI thread.
    // The UI is separate from the reported drive-worker minimum.
    public int MinimumWorkers => Devices.Count;
    public int AllocatedSubworkers => Devices.Sum(d => d.Workers);
    public int RunningOperations => Devices.Sum(d => d.Active);
    public string Summary => $"{Devices.Count} drive workers · {AllocatedSubworkers}/{Budget} I/O subworkers allocated · {RunningOperations} operations running · CPU cap {CpuBudget} · " +
        string.Join("; ", Devices.Select(d => $"{d.Device}: {d.Workers} allocated, {d.Active} running, {d.Queued} queued"));
}
public sealed class StorageWorkContext
{
    private readonly Action<long> _report;
    private readonly SemaphoreSlim _cpuSlots;
    internal StorageWorkContext(Action<long> report, SemaphoreSlim cpuSlots)
    { _report = report; _cpuSlots = cpuSlots; }
    public void Report(long units) { if (units > 0) _report(units); }
    // Read awaits never hold a CPU slot. Only bounded synchronous computation enters here.
    public async ValueTask RunCpuAsync(Action action, CancellationToken token)
    {
        await _cpuSlots.WaitAsync(token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); action(); }
        finally { _cpuSlots.Release(); }
    }
}

// Executes queued operations, never one waiting Task per file. Enqueue may also be called
// from a running operation (discovered directories, or full hashes after sample comparison).
public sealed class AdaptiveStorageScheduler
{
    private sealed class Lane(StorageDevice device)
    {
        public StorageDevice Device = device;
        public ConcurrentQueue<Func<StorageWorkContext, CancellationToken, Task>> Queue = new();
        public int Limit = 1, Active, Probes;
        public long Units, PreviousUnits, LastDispatch;
        public Queue<(long Units, double Seconds)> Window = new();
        public double Rate, Baseline, NextProbe, LastProbe;
    }
    private sealed record AllocationTrial(Lane Receiver, Lane? Donor, double ReceiverRate, double CombinedRate, int AddedSlots);
    private AllocationTrial? _trial;
    private readonly Dictionary<string, Lane> _lanes;
    private readonly TimeSpan _interval;
    private readonly int _windowSamples;
    private bool _started;
    public int Budget { get; }
    public int CpuBudget { get; }
    public int AvailableProcessors { get; }
    public StorageScheduleSnapshot Snapshot { get; private set; }
    public AdaptiveStorageScheduler(IEnumerable<StorageDevice> devices, int processorBudget = 0,
        TimeSpan? sampleInterval = null, int windowSamples = 4, int? availableProcessors = null)
    {
        _lanes = devices.DistinctBy(d => d.Id).ToDictionary(d => d.Id, d => new Lane(d));
        AvailableProcessors = Math.Max(1, availableProcessors ?? CpuCapacity.ReadAvailableProcessors());
        var backgroundLimit = Math.Max(1, AvailableProcessors - 1);
        CpuBudget = processorBudget > 0 ? Math.Min(backgroundLimit, processorBudget) : backgroundLimit;
        Budget = Math.Max(_lanes.Count, CpuBudget);
        _interval = sampleInterval ?? TimeSpan.FromMilliseconds(250);
        _windowSamples = windowSamples;
        if (_interval <= TimeSpan.Zero || windowSamples < 2) throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        Snapshot = Capture();
    }
    public void Enqueue(StorageDevice device, Func<StorageWorkContext, CancellationToken, Task> work) => _lanes[device.Id].Queue.Enqueue(work);
    private StorageScheduleSnapshot Capture() => new(Budget, _lanes.Values.Select(l => new StorageWorkerSnapshot(
        l.Device.Label, l.Limit, l.Active, l.Queue.Count, l.Rate, l.Device.PhysicalIdentityKnown)).ToArray()) { CpuBudget = CpuBudget };

    public async Task RunAsync(CancellationToken token, Action<StorageScheduleSnapshot>? report = null)
    {
        if (_started) throw new InvalidOperationException("A storage schedule can only run once.");
        _started = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var cpuSlots = new SemaphoreSlim(CpuBudget);
        var active = new Dictionary<Task, Lane>();
        var clock = Stopwatch.StartNew();
        var previousTick = 0.0;
        long dispatchSequence = 0;
        var tick = Task.Delay(_interval, stop.Token);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                foreach (var task in active.Keys.Where(t => t.IsCompleted).ToArray())
                {
                    active[task].Active--; active.Remove(task);
                    await task.ConfigureAwait(false);
                }
                // Reserve the first slot for each nonempty device queue before issuing extra work.
                // Historical global-cap notes (superseded by separate I/O and CPU budgets):
                // The UI reserve wins when there are more devices than background slots.
                // Least-recently-served idle queues rotate through those slots without starvation.
                // Every drive now keeps its first I/O subworker; computation shares the CPU gate.
                for (var pass = 0; pass < 2; pass++)
                    foreach (var lane in _lanes.Values.OrderBy(l => l.Active == 0 ? 0 : 1).ThenBy(l => l.Active == 0 ? l.LastDispatch : long.MaxValue).ThenByDescending(l => l.Rate / l.Limit))
                        while (active.Count < Budget && lane.Active < (pass == 0 ? 1 : lane.Limit) && lane.Queue.TryDequeue(out var work))
                        {
                            lane.Active++; lane.LastDispatch = ++dispatchSequence;
                            var operation = work;
                            var context = new StorageWorkContext(units => Interlocked.Add(ref lane.Units, units), cpuSlots);
                            active.Add(Task.Run(async () =>
                            {
                                // Reuse a dispatched subworker for a short batch of tiny jobs. This
                                // avoids a Task/WhenAny/queue-ordering round trip for every folder.
                                var started = Stopwatch.GetTimestamp();
                                for (var count = 0; ; count++)
                                {
                                    stop.Token.ThrowIfCancellationRequested();
                                    await operation(context, stop.Token).ConfigureAwait(false);
                                    if (count >= 31 || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 20 ||
                                        !lane.Queue.TryDequeue(out operation)) break;
                                }
                            }, stop.Token), lane);
                        }
                if (active.Count == 0 && _lanes.Values.All(l => l.Queue.IsEmpty)) break;
                if (tick.IsCompleted)
                {
                    var now = clock.Elapsed.TotalSeconds;
                    Adjust(now, now - previousTick); previousTick = now;
                    Snapshot = Capture(); report?.Invoke(Snapshot);
                    tick = Task.Delay(_interval, stop.Token);
                    continue; // Dispatch newly allocated subworkers immediately, even during a long hash.
                }
                await Task.WhenAny(active.Keys.Append(tick)).ConfigureAwait(false);
            }
            Snapshot = Capture(); report?.Invoke(Snapshot);
        }
        finally
        {
            stop.Cancel();
            try { await Task.WhenAll(active.Keys).ConfigureAwait(false); } catch { /* Observe all workers before releasing their files/cache. */ }
        }
    }

    private double WindowSeconds => _interval.TotalSeconds * _windowSamples;
    private static bool HasDemand(Lane lane) => !lane.Queue.IsEmpty;

    private void Adjust(double now, double seconds)
    {
        foreach (var lane in _lanes.Values)
        {
            var units = Interlocked.Read(ref lane.Units);
            var delta = units - lane.PreviousUnits; lane.PreviousUnits = units;
            // A reduced allocation is measured only after its old operations have drained.
            if (lane.Active > lane.Limit) { lane.Window.Clear(); continue; }
            lane.Window.Enqueue((delta, seconds));
            while (lane.Window.Count > _windowSamples) lane.Window.Dequeue();
            lane.Rate = lane.Window.Sum(s => s.Units) / Math.Max(.001, lane.Window.Sum(s => s.Seconds));
        }

        if (_trial is { } trial)
        {
            var receiver = trial.Receiver;
            var donor = trial.Donor;
            var demandEnded = !HasDemand(receiver) || (donor != null && !HasDemand(donor));
            if (!demandEnded && (receiver.Window.Count < _windowSamples ||
                (donor != null && donor.Window.Count < _windowSamples))) return;
            // Retain a trial only for a measurable gain; noisy/no-gain windows back off.
            // Transfers must improve combined throughput, including the donor's lost capacity.
            var improved = !demandEnded && receiver.Rate >= trial.ReceiverRate * 1.05 &&
                receiver.Rate + (donor?.Rate ?? 0) >= trial.CombinedRate * 1.05;
            if (!improved)
            {
                receiver.Limit = Math.Max(1, receiver.Limit - trial.AddedSlots);
                if (donor != null) donor.Limit++;
                receiver.NextProbe = now + WindowSeconds * 3;
            }
            else receiver.NextProbe = now;
            receiver.Baseline = improved ? receiver.Rate : 0;
            if (!improved) receiver.Window.Clear();
            if (donor != null)
            {
                donor.Baseline = improved ? donor.Rate : 0;
                donor.NextProbe = now + WindowSeconds;
                if (!improved) donor.Window.Clear();
            }
            _trial = null;
        }

        foreach (var lane in _lanes.Values)
        {
            if (!HasDemand(lane) && lane.Active == 0)
            { lane.Limit = 1; lane.Baseline = 0; lane.Window.Clear(); continue; }
            if (lane.Window.Count < _windowSamples) continue;
            if (lane.Limit > 1 && HasDemand(lane) && lane.Baseline > 0 && lane.Rate < lane.Baseline * .85)
            {
                // Workloads and other disk users can change after a successful trial.
                lane.Limit--; lane.Baseline = 0; lane.Window.Clear();
                lane.NextProbe = now + WindowSeconds * 3;
            }
            else if (lane.Baseline == 0) lane.Baseline = lane.Rate;
        }

        // Only one allocation experiment runs at a time so simultaneous changes cannot
        // hide a donor's regression. Oldest probes get another turn as workloads change.
        var candidates = _lanes.Values.Where(l => l.Window.Count >= (l.Probes == 0 ? 2 : _windowSamples) &&
            HasDemand(l) && l.Active >= l.Limit && l.Rate > 0 && now >= l.NextProbe)
            .OrderBy(l => l.LastProbe).ThenByDescending(l => l.Rate / l.Limit);
        foreach (var lane in candidates)
        {
            Lane? donor = null;
            if (_lanes.Values.Sum(l => l.Limit) >= Budget)
            {
                donor = _lanes.Values.Where(l => l != lane && l.Limit > 1 &&
                    l.Window.Count >= _windowSamples && HasDemand(l) && now >= l.NextProbe)
                    .OrderBy(l => l.Rate / l.Limit).FirstOrDefault();
                if (donor == null) continue;
            }
            // Successful growth doubles the allocation while spare capacity exists; transfers
            // still move one slot so the donor's cost can be measured independently.
            var added = donor != null ? 1 : Math.Min(lane.Limit, Budget - _lanes.Values.Sum(l => l.Limit));
            _trial = new(lane, donor, lane.Rate, lane.Rate + (donor?.Rate ?? 0), added);
            if (donor != null) { donor.Limit--; donor.Window.Clear(); }
            lane.Limit += added; lane.Probes++; lane.LastProbe = now; lane.Window.Clear();
            break;
        }
    }
}

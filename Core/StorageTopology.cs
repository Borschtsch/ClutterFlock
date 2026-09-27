using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClutterFlock.Core;

public sealed record StorageDevice(string Id, string Label, bool PhysicalIdentityKnown);
public sealed record StoragePath(string Path, StorageDevice Device);

// Immutable routing table shared by discovery, metadata reads, samples and hashes.
public sealed class StorageTopology
{
    private readonly StoragePath[] _paths;
    public IReadOnlyList<StorageDevice> Devices { get; }
    public int AvailableProcessors { get; }
    public StorageTopology(IEnumerable<StoragePath> paths)
    {
        AvailableProcessors = CpuCapacity.ReadAvailableProcessors();
        _paths = paths.Select(p => p with { Path = PathUtilities.Normalize(p.Path) })
            .OrderByDescending(p => p.Path.Length).ToArray();
        Devices = _paths.Select(p => p.Device).DistinctBy(d => d.Id).ToArray();
    }
    public StorageDevice Resolve(string path) => _paths.First(p => PathUtilities.IsWithin(path, p.Path)).Device;

    public static StorageTopology Discover(IEnumerable<string> roots)
    {
        var volumes = new Dictionary<string, (string Key, int[] Disks)>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<(string Path, string Key, int[] Disks)>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = PathUtilities.Normalize(root);
            var mount = new StringBuilder(32768);
            var volume = GetVolumePathName(path, mount, mount.Capacity) ? mount.ToString() : Path.GetPathRoot(path)!;
            if (!volumes.TryGetValue(volume, out var identity))
            {
                var disks = DiskNumbers(volume);
                // Remote/virtual storage cannot reliably expose its physical disks. Do not claim
                // separate drive letters are independent hardware when Windows cannot identify them.
                var key = volume.StartsWith(@"\\", StringComparison.Ordinal)
                    ? "network:" + volume.Split('\\', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant()
                    : "unknown-local";
                volumes[volume] = identity = (key, disks);
            }
            paths.Add((volume, identity.Key, identity.Disks));
        }
        // Volumes sharing any physical disk share a queue, including spanned volumes.
        var sets = volumes.Values.Where(p => p.Disks.Length > 0).Select(p => p.Disks.ToHashSet()).ToList();
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
                if (sets[i].Overlaps(sets[j])) { sets[i].UnionWith(sets[j]); sets.RemoveAt(j); j = i; }
        return new StorageTopology(paths.DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(p =>
        {
            if (p.Disks.Length == 0) return new StoragePath(p.Path, new(p.Key, p.Key.StartsWith("network:") ? "Network server " + p.Key[8..] : "Storage identity unavailable", false));
            var numbers = sets.First(s => s.Contains(p.Disks[0])).Order().ToArray();
            var id = "disk:" + string.Join(",", numbers);
            return new StoragePath(p.Path, new(id, "Disk " + string.Join(" + ", numbers), true));
        }));
    }

    private static int[] DiskNumbers(string mount)
    {
        var name = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPoint(mount, name, name.Capacity)) return [];
        using var volume = CreateFile(name.ToString().TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (volume.IsInvalid) return [];
        var buffer = new byte[1024];
        while (true)
        {
            var success = DeviceIoControl(volume, 0x00560000, IntPtr.Zero, 0, buffer, buffer.Length, out var returned, IntPtr.Zero);
            if (!success)
            {
                if (Marshal.GetLastWin32Error() != 234 || buffer.Length >= 1024 * 1024) return [];
                buffer = new byte[buffer.Length * 2]; continue;
            }
            var count = BitConverter.ToInt32(buffer, 0);
            if (count <= 0 || 8L + count * 24L > returned) return [];
            return Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(buffer, 8 + i * 24)).Distinct().ToArray();
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNameW")]
    private static extern bool GetVolumePathName(string file, StringBuilder volume, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder volume, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}

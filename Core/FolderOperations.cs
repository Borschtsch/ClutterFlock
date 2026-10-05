using System.IO;
using System.Buffers;
using System.Collections.Concurrent;

namespace ClutterFlock.Core;

public enum FolderAction { DeleteA, DeleteB, MergeToA, MergeToB }
public sealed record FileConflict(string PathA, string PathB, string Reason);
public sealed record BlockingFolder(string Side, string Path);

public sealed class FolderOperationPlan
{
    public FolderAction Action { get; }
    public string Source { get; }
    public string? Destination { get; }
    public int FileCount { get; }
    public long Bytes { get; }
    public IReadOnlyList<FileConflict> Conflicts { get; }
    public IReadOnlyList<BlockingFolder> Subfolders { get; }
    public bool CanExecute => Conflicts.Count == 0 && Subfolders.Count == 0;
    internal string[] SourceSnapshot { get; }
    internal string[]? DestinationSnapshot { get; }
    public string Title => Action switch
    {
        FolderAction.DeleteA => "Delete A", FolderAction.DeleteB => "Delete B",
        FolderAction.MergeToA => "Merge to A", _ => "Merge to B"
    };
    public string Description => Destination == null
        ? $"Permanently delete this leaf folder:\n\n{Source}\n\n{FileCount:N0} direct files · {Bytes:N0} bytes. No subfolders.\n\nThe other folder is kept. This bypasses the Recycle Bin and cannot be undone."
        : $"Move from:\n{Source}\n\nInto:\n{Destination}\n\n{FileCount:N0} direct files · {Bytes:N0} bytes. Neither folder has subfolders.\n\nIdentical files at the same relative path are kept once. All conflicts must be resolved first. No existing file is overwritten. The source folder is removed when empty.\n\nCancellation or an error can leave a partial merge; completed moves are retained.";
    internal FolderOperationPlan(FolderAction action, string source, string? destination, string[] sourceSnapshot, string[]? destinationSnapshot, int files, long bytes, List<FileConflict> conflicts, List<BlockingFolder> subfolders)
    { Action = action; Source = source; Destination = destination; SourceSnapshot = sourceSnapshot; DestinationSnapshot = destinationSnapshot; FileCount = files; Bytes = bytes; Conflicts = conflicts.AsReadOnly(); Subfolders = subfolders.AsReadOnly(); }
}

/// <summary>Real filesystem operations, independent of saved analysis evidence.</summary>
public sealed partial class FolderOperations
{
    public Task<FolderOperationPlan> PrepareAsync(FolderAction action, string a, string b, CancellationToken token = default) => Task.Run(() =>
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        a = PathUtilities.Normalize(a); b = PathUtilities.Normalize(b);
        if (PathUtilities.IsWithin(a, b) || PathUtilities.IsWithin(b, a))
            throw new IOException("Folder actions require separate folders, not equal or nested paths.");
        var source = action is FolderAction.DeleteA or FolderAction.MergeToB ? a : b;
        var destination = action is FolderAction.MergeToA ? a : action is FolderAction.MergeToB ? b : null;
        var sourceSnapshot = Snapshot(source, token, out var files, out var bytes);
        var destinationSnapshot = destination == null ? null : Snapshot(destination, token, out _, out _);
        var subfolders = Directory.GetDirectories(source).Select(p => new BlockingFolder(source == a ? "A" : "B", p)).ToList();
        if (destination != null) subfolders.AddRange(Directory.GetDirectories(destination).Select(p => new BlockingFolder(destination == a ? "A" : "B", p)));
        var conflicts = new List<FileConflict>();
        if (destination != null && subfolders.Count == 0) FindConflicts(a, b, conflicts, token);
        return new FolderOperationPlan(action, source, destination, sourceSnapshot, destinationSnapshot, files, bytes, conflicts, subfolders);
    }, token);

    public async Task ExecuteAsync(FolderOperationPlan plan, IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (!plan.CanExecute) throw new IOException("Resolve subfolders and file conflicts manually before continuing.");
        await Task.Run(() =>
        {
            if (!plan.SourceSnapshot.SequenceEqual(Snapshot(plan.Source, token, out _, out _)) ||
                plan.Destination != null && !plan.DestinationSnapshot!.SequenceEqual(Snapshot(plan.Destination, token, out _, out _)))
                throw new IOException("Folders changed after confirmation was prepared. Review the action again.");
            if (plan.Destination != null)
            {
                var conflicts = new List<FileConflict>();
                FindConflicts(plan.Source, plan.Destination, conflicts, token);
                if (conflicts.Count > 0) throw new IOException("Contents changed: resolve conflicts before merging.");
            }
        }, token);
        token.ThrowIfCancellationRequested();
        if (plan.Destination == null)
        {
            await Task.Run(() => DeleteLeaf(plan.Source, progress, token), token);
        }
        else await Task.Run(() => Merge(plan.Source, plan.Destination, progress, token), token);
    }

    private static string[] Snapshot(string root, CancellationToken token, out int files, out long bytes)
    {
        if (Path.GetPathRoot(root)!.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("A drive root cannot be changed by folder actions.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder unavailable: {root}");
        CheckPath(root);
        var entries = new List<string>();
        files = 0; bytes = 0;
        entries.Add("D|.");
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            token.ThrowIfCancellationRequested(); CheckPath(path);
            if (Directory.Exists(path)) entries.Add("D|" + Path.GetFileName(path));
            else
            {
                var info = new FileInfo(path); files++; bytes += info.Length;
                entries.Add($"F|{Path.GetFileName(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            }
        }
        return entries.Order(StringComparer.Ordinal).ToArray();
    }

    private static void CheckPath(string path)
    {
        // Reject links in both the selected tree and its ancestors, including junctions.
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Folder actions do not follow symbolic links or junctions: {current}");
    }

    public async Task DeleteFileAsync(string path)
    {
        path = PathUtilities.Normalize(path);
        await Task.Run(() =>
        {
            EnsureLeaf(Path.GetDirectoryName(path)!);
            CheckPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("File unavailable.", path);
            File.Delete(path);
        });
    }

    private static void DeleteLeaf(string path, IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); EnsureLeaf(path);
        foreach (var child in Directory.GetFileSystemEntries(path))
        {
            token.ThrowIfCancellationRequested(); EnsureLeaf(path); CheckPath(child);
            progress?.Report($"Deleting {child}"); File.Delete(child);
        }
        Directory.Delete(path, recursive: false);
    }

    private static void EnsureLeaf(string path)
    {
        CheckPath(path);
        if (Directory.EnumerateDirectories(path).Any()) throw new IOException($"Subfolders require manual review first: {path}");
    }

    private static void FindConflicts(string a, string b, List<FileConflict> conflicts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); CheckPath(a); CheckPath(b);
        var found = new ConcurrentBag<FileConflict>();
        try
        {
        Parallel.ForEach(Directory.EnumerateFileSystemEntries(a), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, pathA =>
        {
            token.ThrowIfCancellationRequested();
            var pathB = Path.Combine(b, Path.GetFileName(pathA));
            if (!File.Exists(pathB) && !Directory.Exists(pathB)) return;
            CheckPath(pathA); CheckPath(pathB);
            var dirA = Directory.Exists(pathA); var dirB = Directory.Exists(pathB);
            if (dirA != dirB) found.Add(new(pathA, pathB, "File / folder name conflict — resolve in Explorer"));
            else if (dirA) throw new IOException("Subfolders must be resolved manually before merging.");
            else
            {
                using var left = OpenForComparison(pathA);
                using var right = OpenForComparison(pathB);
                if (!SameContents(left, right, token)) found.Add(new(pathA, pathB, "Different contents"));
            }
        });
        }
        catch (AggregateException ex)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
        }
        conflicts.AddRange(found.OrderBy(c => c.PathA, StringComparer.OrdinalIgnoreCase));
    }

    private static FileStream OpenForComparison(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static bool SameContents(Stream left, Stream right, CancellationToken token)
    {
        if (left.Length != right.Length) return false;
        const int size = 128 * 1024;
        var a = ArrayPool<byte>.Shared.Rent(size); var b = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            for (long remaining = left.Length; remaining > 0; remaining -= size)
            {
                var count = (int)Math.Min(size, remaining);
                // Exact comparison stops at the first differing block; no redundant digests.
                Task.WhenAll(left.ReadExactlyAsync(a.AsMemory(0, count), token).AsTask(),
                    right.ReadExactlyAsync(b.AsMemory(0, count), token).AsTask()).GetAwaiter().GetResult();
                if (!a.AsSpan(0, count).SequenceEqual(b.AsSpan(0, count))) return false;
            }
            return true;
        }
        finally { ArrayPool<byte>.Shared.Return(a); ArrayPool<byte>.Shared.Return(b); }
    }

    private static void Merge(string source, string destination, IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); EnsureLeaf(source); EnsureLeaf(destination);
        // Materialize each directory before moves; never recursively delete a source tree.
        foreach (var path in Directory.GetFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested(); EnsureLeaf(source); EnsureLeaf(destination); CheckPath(path);
            var target = Path.Combine(destination, Path.GetFileName(path));
            progress?.Report($"Merging {path}");
            if (Directory.Exists(path))
            {
                throw new IOException("Subfolders must be resolved manually before merging.");
            }
            else
            {
                if (File.Exists(target))
                {
                    CheckPath(target);
                    // Read current contents with write/delete sharing denied, not cached hashes.
                    using var from = OpenForComparison(path);
                    using var to = OpenForComparison(target);
                    var identical = SameContents(from, to, token);
                    token.ThrowIfCancellationRequested();
                    if (identical)
                    {
                        from.Dispose();
                        File.Delete(path);
                        continue;
                    }
                    throw new IOException($"New content conflict: {target}");
                }
                if (Directory.Exists(target)) throw new IOException($"New file/folder conflict: {target}");
                token.ThrowIfCancellationRequested();
                File.Move(path, target, overwrite: false);
            }
        }
        token.ThrowIfCancellationRequested(); CheckPath(source);
        Directory.Delete(source, recursive: false);
    }
}

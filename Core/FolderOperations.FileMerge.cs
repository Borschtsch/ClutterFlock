using System.IO;

namespace ClutterFlock.Core;

public sealed class FileMergePlan
{
    public string Source { get; }
    public string Destination { get; }
    public bool DestinationExists { get; }
    internal (long Size, DateTime Modified) SourceStamp { get; }
    internal (long Size, DateTime Modified)? DestinationStamp { get; }
    internal FileMergePlan(string source, string destination, (long, DateTime) sourceStamp, (long, DateTime)? destinationStamp)
    { Source = source; Destination = destination; SourceStamp = sourceStamp; DestinationStamp = destinationStamp; DestinationExists = destinationStamp != null; }
    public string Description => $"ONLY THE DISPLAYED FILE PAIR\n\nFrom:\n{Source}\n\nTo:\n{Destination}\n\n"
        + (DestinationExists ? "The files have identical contents. Keep the destination file and permanently delete the source copy."
            : "Move this one file to the destination. No existing file will be overwritten.")
        + "\n\nOther files and both folders are kept. This action cannot be undone from this application.";
}

public sealed partial class FolderOperations
{
    public Task<FileMergePlan> PrepareFileMergeAsync(string source, string destination, CancellationToken token = default) => Task.Run(() =>
    {
        source = PathUtilities.Normalize(source); destination = PathUtilities.Normalize(destination);
        ValidateFileMergePaths(source, destination);
        var fromStamp = FileStamp(source);
        (long, DateTime)? toStamp = File.Exists(destination) ? FileStamp(destination) : null;
        using var from = OpenForComparison(source);
        if (toStamp != null)
        {
            using var to = OpenForComparison(destination);
            if (!SameContents(from, to, token))
                throw new IOException("Merge blocked: the displayed files have different contents. Inspect A and B, explicitly delete the unwanted version, then retry. Nothing was changed.");
        }
        token.ThrowIfCancellationRequested();
        return new FileMergePlan(source, destination, fromStamp, toStamp);
    }, token);

    public Task ExecuteFileMergeAsync(FileMergePlan plan, CancellationToken token = default) => Task.Run(() =>
    {
        ValidateFileMergePaths(plan.Source, plan.Destination);
        if (FileStamp(plan.Source) != plan.SourceStamp || File.Exists(plan.Destination) != plan.DestinationExists
            || plan.DestinationExists && FileStamp(plan.Destination) != plan.DestinationStamp)
            throw new IOException("Files changed after confirmation was prepared. Review the displayed pair again.");
        token.ThrowIfCancellationRequested();
        if (plan.DestinationExists)
        {
            using var source = OpenForComparison(plan.Source);
            using var destination = OpenForComparison(plan.Destination);
            if (!SameContents(source, destination, token))
                throw new IOException("Contents changed: resolve the displayed file conflict before merging.");
            token.ThrowIfCancellationRequested();
            EnsureLeaf(Path.GetDirectoryName(plan.Source)!); EnsureLeaf(Path.GetDirectoryName(plan.Destination)!);
            source.Dispose();
            File.Delete(plan.Source);
        }
        else File.Move(plan.Source, plan.Destination, overwrite: false);
    }, token);

    private static void ValidateFileMergePaths(string source, string destination)
    {
        var from = Path.GetDirectoryName(source)!; var to = Path.GetDirectoryName(destination)!;
        if (PathUtilities.IsWithin(from, to) || PathUtilities.IsWithin(to, from))
            throw new IOException("File merge requires separate, non-nested folders.");
        EnsureLeaf(from); EnsureLeaf(to); CheckPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Source file unavailable.", source);
        if (Directory.Exists(destination)) throw new IOException("A folder occupies the destination filename.");
        if (File.Exists(destination)) CheckPath(destination);
    }

    private static (long Size, DateTime Modified) FileStamp(string path)
    {
        var info = new FileInfo(path);
        return (info.Length, info.LastWriteTimeUtc);
    }
}

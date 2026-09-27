using System.IO;
using ClutterFlock.Models;

namespace ClutterFlock.Core;

public sealed record FolderEvidenceBatch(string LeftFolder, string RightFolder, List<FileMatch> Files);

/// <summary>Coalesces evidence on producer threads; rendering never blocks verification.</summary>
public sealed class AnalysisResultBuffer
{
    private readonly object _gate = new();
    private readonly Dictionary<(string, string), List<FileMatch>> _pending = new();
    private readonly Queue<(string, string)> _order = new();
    private readonly Dictionary<string, string> _folders = new(StringComparer.OrdinalIgnoreCase);
    public bool HasChanges { get { lock (_gate) return _order.Count != 0; } }

    public void Add(IReadOnlyList<FileMatch> matches)
    {
        lock (_gate)
        {
            string Folder(string file)
            {
                if (!_folders.TryGetValue(file, out var folder)) _folders[file] = folder = Path.GetDirectoryName(file)!;
                return folder;
            }
            foreach (var match in matches)
            {
                var key = (Folder(match.PathA), Folder(match.PathB));
                if (!_pending.TryGetValue(key, out var files))
                { _pending[key] = files = new(); _order.Enqueue(key); }
                files.Add(match);
            }
        }
    }

    public List<FolderEvidenceBatch> Take(int maxPairs = 128)
    {
        if (maxPairs < 1) throw new ArgumentOutOfRangeException(nameof(maxPairs));
        var result = new List<FolderEvidenceBatch>();
        lock (_gate)
            while (result.Count < maxPairs && _order.TryDequeue(out var key))
            {
                var files = _pending[key]; _pending.Remove(key);
                // Transfer ownership, without copying or walking large evidence lists.
                result.Add(new(key.Item1, key.Item2, files));
            }
        return result;
    }
}

using System.IO;

namespace ClutterFlock.Core
{
    internal static class PathUtilities
    {
        public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        public static bool IsWithin(string path, string root)
        {
            path = Normalize(path);
            root = Normalize(root);
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.IO;

namespace PSConverter
{
    internal static class PathSafety
    {
        public static string LocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] {'\r','\n','\0','"'}) >= 0)
                throw new ArgumentException("Select a valid local path.");
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
                throw new ArgumentException("Network paths, device paths and alternate data streams are not supported.");
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.IndexOf(':', 2) >= 0)
                throw new ArgumentException("Network paths, device paths and alternate data streams are not supported.");
            RejectReparsePoints(full);
            return full;
        }

        public static void RejectReparsePoints(string path)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Choose a path without symbolic links or directory junctions.");
                current = Path.GetDirectoryName(current);
            }
        }

        public static string NewWorkspace(string directory)
        {
            string root = Path.Combine(directory, ".psconverter-work");
            RejectReparsePoints(root);
            Directory.CreateDirectory(root);
            string workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            return workspace;
        }

        public static string Publish(string stagedFile, string outputDirectory, string name, string extension)
        {
            // File.Move fails atomically on a collision; never reserve by deleting or truncating a destination.
            for (int suffix = 0; ; suffix++)
            {
                string file = name + (suffix == 0 ? "" : " (" + suffix + ")") + extension;
                string candidate = Path.Combine(outputDirectory, file);
                RejectReparsePoints(candidate);
                try { File.Move(stagedFile, candidate); return candidate; }
                catch (IOException) { if (!File.Exists(candidate)) throw; }
            }
        }
    }
}
